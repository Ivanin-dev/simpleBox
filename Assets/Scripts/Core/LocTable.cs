using System.Collections.Generic;

namespace SimpleBox.Core
{
    /// <summary>
    /// Russian → English translations (keys are the exact Russian strings used in code and scene labels).
    /// Add a pair here when adding a player-facing Russian string; a missing pair shows the Russian text.
    /// </summary>
    internal static class LocTable
    {
        internal static Dictionary<string, string> BuildEnglish()
        {
            var d = new Dictionary<string, string>(400);

            // ---- Main menu / about / loading
            d["ИГРАТЬ"] = "PLAY";
            d["ПРОДОЛЖИТЬ"] = "CONTINUE";
            d["МУЛЬТИПЛЕЕР"] = "MULTIPLAYER";
            d["ЗАГРУЗИТЬ"] = "LOAD";
            d["НАСТРОЙКИ"] = "SETTINGS";
            d["О ИГРЕ"] = "ABOUT";
            d["Ник"] = "Nickname";
            d["Твой ник"] = "Your nickname";
            d["физическая песочница"] = "physics sandbox";
            d["Физическая песочница для телефона.\nСтрой, взрывай, катайся — один или с друзьями."] =
                "A physics sandbox for your phone.\nBuild, blow up, ride — alone or with friends.";
            d["ПОЛИТИКА КОНФИДЕНЦИАЛЬНОСТИ"] = "PRIVACY POLICY";
            d["Загрузчик не настроен"] = "Loader is not configured";
            d["Сцена игры не найдена: "] = "Game scene not found: ";
            d["Физган: USE хватает проп, ALT замораживает его в воздухе"] = "Physgun: USE grabs a prop, ALT freezes it in the air";
            d["Сварка соединяет два пропа в одну конструкцию"] = "Weld joins two props into one build";
            d["Двигатели и оси можно привязать к кнопкам 1–4"] = "Thrusters and axles can be bound to buttons 1–4";
            d["Сядь на стул — джойстик будет управлять кнопками 1–4"] = "Sit on a seat — the joystick drives buttons 1–4";
            d["Дубликатор копирует целую конструкцию"] = "The duplicator copies a whole build";
            d["Взрывная бочка детонирует от удара или детонатора"] = "An explosive barrel goes off on impact or with the detonator";
            d["UNDO отменяет последнее действие"] = "UNDO reverts your last action";
            d["Настройки графики и управления — кнопка MENU"] = "Graphics and control settings — MENU button";

            // ---- Panels
            d["Пропы"] = "Props";
            d["Сохранения"] = "Saves";
            d["Настройки"] = "Settings";
            d["Мультиплеер"] = "Multiplayer";
            d["Игроки"] = "Players";
            d["ИГРОКИ"] = "PLAYERS";
            d["Нет игроков"] = "No players";
            d["Код сессии"] = "Session code";
            d["СБРОС"] = "RESET";
            d["ОБУЧЕНИЕ"] = "TUTORIAL";
            d["В МЕНЮ"] = "MENU";
            d["Качество применится после перезапуска игры"] = "Quality applies after restarting the game";

            // ---- Settings
            d["Качество графики"] = "Graphics quality";
            d["Лимит FPS"] = "FPS limit";
            d["Чувствительность"] = "Sensitivity";
            d["Инверсия Y"] = "Invert Y";
            d["Громкость"] = "Volume";
            d["Размер кнопок"] = "Button size";
            d["Счётчик FPS"] = "FPS counter";
            d["Язык"] = "Language";
            d["Русский"] = "Русский";
            d["Авто"] = "Auto";
            d["Минимум"] = "Minimum";
            d["Рекомендуемое"] = "Recommended";
            d["По профилю"] = "By profile";
            d["Выкл"] = "Off";
            d["Вкл"] = "On";
            d["ВКЛ"] = "ON";
            d["ВЫКЛ"] = "OFF";

            // ---- Saves
            d["Слот 1"] = "Slot 1";
            d["Слот 2"] = "Slot 2";
            d["Слот 3"] = "Slot 3";
            d["Слот "] = "Slot ";
            d["пусто"] = "empty";
            d["повреждён"] = "corrupted";
            d["об."] = "obj.";
            d["Сохранено"] = "Saved";
            d["Нечего сохранять"] = "Nothing to save";
            d["Загружено"] = "Loaded";
            d["Слот пуст"] = "Slot is empty";
            d["Ошибка сохранения"] = "Save failed";
            d["Не удалось загрузить"] = "Could not load";
            d["Загрузка…"] = "Loading…";

            // ---- Session / players
            d["Не в сети"] = "Offline";
            d["Создание сессии…"] = "Creating session…";
            d["Хост. Код сессии: "] = "Host. Session code: ";
            d["Ошибка создания: "] = "Create failed: ";
            d["Введите код сессии"] = "Enter a session code";
            d["Подключение…"] = "Connecting…";
            d["Подключено к "] = "Connected to ";
            d["Ошибка подключения: "] = "Connection failed: ";
            d["Выход…"] = "Leaving…";
            d["Сессия завершена"] = "Session ended";
            d["Хост исключил вас из игры"] = "The host removed you from the game";
            d["Игрок"] = "Player";
            d["Игрок "] = "Player ";
            d["ЖАЛОБА"] = "REPORT";
            d["БЛОК"] = "BLOCK";
            d["РАЗБЛОК"] = "UNBLOCK";
            d["КИК"] = "KICK";
            d["  (ты)"] = "  (you)";
            d["  [хост]"] = "  [host]";
            d["{0} исключён"] = "{0} was kicked";
            d["{0} заблокирован"] = "{0} is blocked";
            d["{0} разблокирован"] = "{0} is unblocked";
            d["Жалоба на {0}: допиши письмо и отправь. Игрок заблокирован"] = "Report on {0}: finish the email and send it. Player blocked";
            d["Жалоба на {0} записана. Игрок заблокирован"] = "Report on {0} saved. Player blocked";
            d["Профиль игрока ещё не загружен, попробуй через секунду"] = "Player profile is not loaded yet, try again in a second";
            d["SimpleBox: жалоба на игрока"] = "SimpleBox: player report";
            d["Сессия"] = "Session";
            d["Время"] = "Time";
            d["Версия"] = "Version";
            d["Опишите, что произошло:"] = "Describe what happened:";

            // ---- Onboarding
            d["Иди: джойстик в левой половине экрана"] = "Walk: joystick on the left half of the screen";
            d["Осмотрись: веди пальцем по правой половине экрана"] = "Look around: drag your finger on the right half of the screen";
            d["Создай проп: кнопка SPAWN. Тап по панели пропа справа вверху — выбор пропа"] =
                "Spawn a prop: SPAWN button. Tap the prop panel at the top right to pick a prop";
            d["Физган: наведи прицел на проп и нажми USE — проп в руках. USE ещё раз — отпустить, ALT — заморозить"] =
                "Physgun: aim at a prop and press USE to grab it. USE again to drop, ALT to freeze";
            d["Смени инструмент: кнопка TOOL. Подсказка инструмента — внизу экрана"] =
                "Switch tool: TOOL button. The tool hint is at the bottom of the screen";
            d["Готово! UNDO отменяет, SAVE — сохранения, MENU — настройки и повтор обучения"] =
                "Done! UNDO reverts, SAVE — saves, MENU — settings and the tutorial again";

            // ---- HUD / toasts
            d["НЕТ МЕСТА"] = "NO SPACE";
            d["ЛИМИТ"] = "LIMIT";
            d["Полёт: ВКЛ"] = "Flight: ON";
            d["Полёт: ВЫКЛ"] = "Flight: OFF";
            d["Полёт запрещён на сервере"] = "Flight is disabled on this server";
            d["Полёт недоступен (нет кнопок FLY / DOWN)"] = "Flight unavailable (no FLY / DOWN buttons)";
            d["Нельзя выйти из полёта внутри объекта"] = "Cannot stop flying inside an object";
            d["Отменено: проп"] = "Undone: prop";
            d["Отменено: двигатель"] = "Undone: thruster";
            d["Отменено: сварка"] = "Undone: weld";
            d["Нечего отменять"] = "Nothing to undo";
            d["Нажмите CLEAR ещё раз — удалить все ваши пропы"] = "Press CLEAR again to remove all your props";
            d["Все ваши пропы удалены"] = "All your props removed";
            d["Тяга: ВКЛ"] = "Thrust: ON";
            d["Тяга: ВЫКЛ"] = "Thrust: OFF";
            d["Отмена…"] = "Undoing…";
            d["Тяга: переключено"] = "Thrust: toggled";
            d["Сервер: это чужой объект"] = "Server: not your object";
            d["Сервер: лимит"] = "Server: limit reached";
            d["Сервер: нет места"] = "Server: no space";
            d["Сервер: объект уже удалён"] = "Server: object already removed";
            d["Сервер: уже сварены"] = "Server: already welded";
            d["Сервер отклонил действие"] = "Server rejected the action";

            // ---- Props
            d["Ящик"] = "Crate";
            d["Мяч"] = "Ball";
            d["Бочка"] = "Barrel";
            d["Кресло"] = "Seat";
            d["Доска"] = "Plank";
            d["Балка"] = "Beam";
            d["Плита"] = "Plate";
            d["Большой ящик"] = "Big crate";
            d["Колесо"] = "Wheel";
            d["Мячик"] = "Small ball";
            d["Взрывная бочка"] = "Explosive barrel";
            d["Регдолл"] = "Ragdoll";

            // ---- Tools (names)
            d["Физган"] = "Physgun";
            d["Удалитель"] = "Remover";
            d["Двигатель"] = "Thruster";
            d["Сварка"] = "Weld";
            d["Верёвка"] = "Rope";
            d["Ось / колесо"] = "Axle / wheel";
            d["Детонатор"] = "Detonator";
            d["Цвет"] = "Color";
            d["Дубликатор"] = "Duplicator";
            d["Поршень"] = "Hydraulic";

            // ---- Tool hints (shared)
            d["Нужен проп под прицелом"] = "Aim at a prop";
            d["Это чужой проп"] = "This prop belongs to someone else";
            d["Сброс"] = "Reset";
            d["Связи сняты"] = "Constraints removed";
            d["У пропа нет связей"] = "The prop has no constraints";
            d["Эти объекты уже связаны"] = "These objects are already connected";
            d["Лимит связей"] = "Constraint limit";
            d["Нельзя прикрепить объект к самому себе"] = "Cannot attach an object to itself";
            d["Сюда прикрепить нельзя"] = "Cannot attach here";
            d["Объект 1 удалён — сброс"] = "Object 1 was removed — reset";
            d["Точка 1 выбрана — выберите точку 2 (проп или мир)   ALT: сброс"] = "Point 1 set — pick point 2 (prop or world)   ALT: reset";
            d["Нет"] = "None";
            d["Скорость"] = "Speed";

            // Physgun
            d["USE: взять / отпустить   ALT: заморозить / разморозить"] = "USE: grab / drop   ALT: freeze / unfreeze";
            d["ROTATE + свайп: вращать   полоса справа: дальше / ближе   ALT: заморозить"] =
                "ROTATE + swipe: rotate   right strip: farther / closer   ALT: freeze";
            d["Шаг поворота"] = "Rotation step";
            d["Свободно"] = "Free";

            // Remover
            d["USE: удалить проп под прицелом"] = "USE: remove the prop you aim at";
            d["Этот объект нельзя удалить"] = "This object cannot be removed";

            // Thruster
            d["USE: поставить двигатель   ALT: вкл/выкл двигатели пропа   THRUST: все"] =
                "USE: place thruster   ALT: prop thrusters on/off   THRUST: all";
            d["Двигатель установлен"] = "Thruster placed";
            d["Лимит двигателей на сцене"] = "Thruster limit reached";
            d["Двигатели пропа: ВКЛ"] = "Prop thrusters: ON";
            d["Двигатели пропа: ВЫКЛ"] = "Prop thrusters: OFF";
            d["На пропе нет двигателей"] = "The prop has no thrusters";
            d["Тяга"] = "Thrust";
            d[" Н"] = " N";
            d["Автозапуск"] = "Auto start";
            d["Кнопка (держать)"] = "Button (hold)";

            // Weld
            d["USE: выбрать объект 1   ALT: снять сварку с пропа"] = "USE: pick object 1   ALT: remove welds from the prop";
            d["Объект 1 выбран — выберите второй (ALT — сброс)"] = "Object 1 picked — pick the second (ALT — reset)";
            d["Сварено"] = "Welded";
            d["Приварено к миру"] = "Welded to the world";
            d["Нельзя сварить объект с самим собой"] = "Cannot weld an object to itself";
            d["Эти объекты уже сварены"] = "These objects are already welded";
            d["Лимит сварок у пропа"] = "Weld limit for this prop";
            d["Лимит сварок на сцене"] = "Weld limit reached";
            d["Сюда приварить нельзя"] = "Cannot weld here";
            d["Сварка снята"] = "Weld removed";
            d["У пропа нет сварки"] = "The prop has no welds";
            d["Прочность"] = "Strength";
            d["Обычная"] = "Normal";
            d["Прочная"] = "Strong";
            d["Неразрывная"] = "Unbreakable";
            d["Сварка к миру"] = "Weld to world";

            // Rope
            d["USE: точка 1 на пропе   ALT: снять связи с пропа"] = "USE: point 1 on a prop   ALT: remove the prop's constraints";
            d["Связано"] = "Connected";
            d["Нельзя связать объект с самим собой"] = "Cannot connect an object to itself";
            d["Сюда привязать нельзя"] = "Cannot tie here";
            d["Тип"] = "Type";
            d["Резинка"] = "Elastic";
            d["Запас длины"] = "Slack";
            d["Привязка к миру"] = "Tie to world";

            // Axis
            d["USE: колесо (точка = ось)   затем основа   ALT: снять связи"] = "USE: wheel (point = axle)   then base   ALT: remove constraints";
            d["Колесо выбрано — выберите основу (проп или мир)   ALT: сброс"] = "Wheel picked — pick the base (prop or world)   ALT: reset";
            d["Ось создана"] = "Axle created";
            d["Колесо удалено — сброс"] = "Wheel was removed — reset";
            d["Кнопка вперёд"] = "Forward button";
            d["Кнопка назад"] = "Backward button";
            d["Вперёд"] = "Forward";
            d["Назад"] = "Backward";
            d[" °/с"] = " °/s";
            d["Сила мотора"] = "Motor force";
            d["Выравнивать ось"] = "Align axle";

            // Hydraulic
            d["USE: точка 1 на пропе, затем точка 2   ALT: снять связи"] = "USE: point 1 on a prop, then point 2   ALT: remove constraints";
            d["Поршень создан"] = "Hydraulic created";
            d["Точки слишком близко"] = "Points are too close";
            d["Кнопка выдвинуть"] = "Extend button";
            d["Кнопка втянуть"] = "Retract button";
            d["Выдвинуть"] = "Extend";
            d["Втянуть"] = "Retract";
            d["Ход"] = "Stroke";
            d[" м"] = " m";
            d[" м/с"] = " m/s";

            // Duplicator
            d["ALT: скопировать постройку под прицелом"] = "ALT: copy the build you aim at";
            d["Копирование…"] = "Copying…";
            d["Скопировано. USE: вставить, ALT: скопировать другую"] = "Copied. USE: paste, ALT: copy another";
            d["Нечего копировать"] = "Nothing to copy";
            d["Наведите на поверхность"] = "Aim at a surface";
            d["Вставлено"] = "Pasted";
            d["Не помещается"] = "Does not fit";
            d["Лимит пропов"] = "Prop limit";
            d["Не удалось вставить"] = "Could not paste";

            // Color
            d["USE: покрасить   ALT: вернуть исходный цвет   SET: выбрать цвет"] = "USE: paint   ALT: original color   SET: pick color";
            d["Покрашено"] = "Painted";
            d["Исходный цвет"] = "Original color";
            d["Белый"] = "White";
            d["Чёрный"] = "Black";
            d["Серый"] = "Gray";
            d["Красный"] = "Red";
            d["Оранжевый"] = "Orange";
            d["Жёлтый"] = "Yellow";
            d["Зелёный"] = "Green";
            d["Голубой"] = "Light blue";
            d["Синий"] = "Blue";
            d["Фиолетовый"] = "Purple";
            d["Розовый"] = "Pink";
            d["Коричневый"] = "Brown";

            // Detonator
            d["USE: взорвать взрывную бочку под прицелом"] = "USE: detonate the explosive barrel you aim at";
            d["Бум!"] = "Boom!";
            d["Это не взрывчатка"] = "That is not an explosive";
            d["Нужна взрывная бочка под прицелом"] = "Aim at an explosive barrel";

            return d;
        }
    }
}
