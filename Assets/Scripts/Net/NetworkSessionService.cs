using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace SimpleBox.Net
{
    /// <summary>
    /// Unity Multiplayer Services session (relay, join by code). Host = this device runs the authority;
    /// Join = connect to a host by its short code. The SDK configures UnityTransport for Relay and starts
    /// NetworkManager as host / client.
    /// Requires the project to be linked to Unity Cloud (Project Settings → Services) with Relay enabled.
    /// All calls are user actions (async, may allocate).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkSessionService : MonoBehaviour
    {
        [Header("Session")]
        [SerializeField, Range(2, 8)] private int maxPlayers = 4;

        private ISession _session;
        private bool _busy;
        private bool _servicesReady;

        /// <summary>Human-readable status line.</summary>
        public event Action<string> StatusChanged;
        /// <summary>Session code (empty when not in a session).</summary>
        public event Action<string> CodeChanged;

        public bool IsBusy => _busy;
        public bool IsInSession => _session != null;
        public string Code => _session != null ? _session.Code : string.Empty;

        private async Task EnsureServicesAsync()
        {
            if (_servicesReady) return;
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
            _servicesReady = true;
        }

        public async void Host()
        {
            if (_busy || _session != null) return;
            _busy = true;
            Status("Создание сессии…");
            try
            {
                await EnsureServicesAsync();
                SessionOptions options = new SessionOptions { MaxPlayers = maxPlayers, IsPrivate = true }.WithRelayNetwork();
                _session = await MultiplayerService.Instance.CreateSessionAsync(options);
                Hook(_session);
                WarnIfNotListening();
                Status("Хост. Код сессии: " + _session.Code);
                CodeChanged?.Invoke(_session.Code);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                _session = null;
                Status("Ошибка создания: " + e.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        public async void Join(string code)
        {
            if (_busy || _session != null) return;
            if (string.IsNullOrWhiteSpace(code))
            {
                Status("Введите код сессии");
                return;
            }

            _busy = true;
            Status("Подключение…");
            try
            {
                await EnsureServicesAsync();
                _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
                Hook(_session);
                WarnIfNotListening();
                Status("Подключено к " + _session.Code);
                CodeChanged?.Invoke(_session.Code);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                _session = null;
                Status("Ошибка подключения: " + e.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        public async void Leave()
        {
            if (_busy || _session == null) return;
            _busy = true;
            Status("Выход…");
            ISession session = _session;
            try
            {
                Unhook(session);
                await session.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
            finally
            {
                _session = null;
                ShutdownNetwork();
                _busy = false;
                Status("Не в сети");
                CodeChanged?.Invoke(string.Empty);
            }
        }

        private void Hook(ISession session)
        {
            session.RemovedFromSession += OnSessionEnded;
            session.Deleted += OnSessionEnded;
        }

        private void Unhook(ISession session)
        {
            if (session == null) return;
            session.RemovedFromSession -= OnSessionEnded;
            session.Deleted -= OnSessionEnded;
        }

        private void OnSessionEnded()
        {
            Unhook(_session);
            _session = null;
            ShutdownNetwork();
            Status("Сессия завершена");
            CodeChanged?.Invoke(string.Empty);
        }

        private static void ShutdownNetwork()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening && !nm.ShutdownInProgress) nm.Shutdown();
        }

        private void WarnIfNotListening()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
                Debug.LogWarning($"[{nameof(NetworkSessionService)}] Session created but NetworkManager is not listening. " +
                                 "Check that the scene has a NetworkManager with UnityTransport.", this);
        }

        private void Status(string message) => StatusChanged?.Invoke(message);

        private void OnDestroy()
        {
            Unhook(_session);
            _session = null;
        }
    }
}
