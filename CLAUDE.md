# New Vision — заметки для Claude

New Vision — MR-приложение для Meta Quest 3 на основе Meta Unity-PassthroughCameraApiSamples (сцена
`MultiObjectDetection`). Оно распознаёт объекты (YOLO) в passthrough, отвечает на вопрос «что это?» через Gemini,
сканирует QR и штрихкоды, показывает живые русские субтитры, «запоминает» объекты, умеет показывать 2D-компаньона,
у него есть панель на запястье в стиле киберпанка и BLE-стик M5StickC Plus2 на шлеме.
`AGENTS.md` и `GEMINI.md` — оригинальные заметки Meta о сэмпле. Где они расходятся с этим файлом, верен этот.

## Пользователь и стиль работы
- Общение на **русском**. Пользователь начинающий: объясняй пошагово и простыми словами.
- В долгих операциях (сборка, adb screencap ~9 с на кадр, прошивка) сообщай этап и примерное время, не молчи.
- Unity Editor пользователь открывает сам. Batchmode-компиляция не работает, пока редактор открыт:
  тогда смотри `%LOCALAPPDATA%/Unity/Editor/Editor.log`.

## Git — строгие правила
- Коммиты идут **без** трейлера `Co-Authored-By: Claude` и без других упоминаний Claude. Автор берётся из локального
  конфига (`Skerpsh`). Никогда не придумывай git-identity: если её нет, спроси.
- Рабочая ветка — `main`, remote `origin` = `github.com/Skerpsh/New-Vision` (приватный).
  `meta-main` отслеживает `upstream` (Meta), push туда отключён. **Никогда не пушить историю Meta/upstream** и не
  добавлять сотрудников Meta в контрибьюторы.
- Пушить **только по просьбе** пользователя.
- Перед каждым коммитом проверяй staged diff на секреты (keystore, пароли, токены, API-ключи).
- `*.onnx`, `*.sentis` и нативные `.so` хранятся в Git LFS.
- В README в благодарностях только Kyrylo Kuzyk и Jordi Ramon (только имена).

## Стек
- Unity 6000.0.66f2, MRUK 85, Unity Inference Engine 2.2.1, OpenXR single-pass instanced, Vulkan, Linear color space.
- Управление **только руками** (`handTrackingSupport` = HandsOnly, контроллеры убраны), skeleton OpenXR.
- Пакет `com.newvision.app`. Меню **New Vision > Apply App Settings** задаёт имя, id и иконку (`Assets/NewVision`).
- Сборка: `-executeMethod NewVision.Editor.NewVisionBuild.BuildAndroid` (инкрементальная ~2–4 мин), установка через
  `adb -s <ip>:5555`. adb лежит в platform-tools Android SDK из Unity (глобального adb нет).
- Логи: `Tools/quest_logs.ps1` (после перезагрузки шлема нужен `-Setup` по кабелю, adb tcpip сбрасывается).
  Строки `[NewVision] ...` показывают тайминги, FPS берётся из строк VrApi в logcat.

## Структура (`Assets/PassthroughCameraApiSamples/MultiObjectDetection/`)
- `SentisInference/`: запуск YOLO (`SentisInferenceRunManager`) и UI рамок (`SentisInferenceUiManager`).
- `ObjectInfo/`: «What is this?» (GeminiClient, ObjectInfoView, ObjectInfoLookup), CodeScanner (ZXing), LinkPreview, WifiConnector.
- `Stick/`: StickClient (BLE), HandTracker, LaptopGestures, WristPanel (вкладки 01 GENERAL / 02 STICK / 03 SYS + CLASSES, FIND).
- `Style/`: NvStyle (6 тем, уровень FX, Tektur), NvBrackets, NvGlitch.
- `Memory/`: ObjectMemory (места объектов за сессию, поиск). `Speech/`: субтитры через sherpa-onnx.
- `Companion/`: 2D-компаньон (порт шейдера capri-desktop, лицензия **PolyForm NC**, не AGPL; арт в gitignore).
- `Browser/`: встроенный WebView (WIP, на паузе).
- Android-плагины: `Assets/Plugins/Android/` (NewVisionBleLink.java, NewVisionWebView.java, libs sherpa/onnxruntime).
- `Tools/`: export_model.py (экспорт YOLO/YOLO-World), get_speech_model.ps1, quest_logs.ps1, test_gemini.py.
- `Firmware/M5Stick/`: прошивка стика (PlatformIO, espressif32@6.9.0, NimBLE-Arduino 1.4) — это основная копия.
- `docs/`: арт для README рендерится из `docs/art/*.html` через `docs/art/render.ps1` (headless Edge).
- `graphify-out/`: офлайн-граф кода (`graphify update .`).

## Важные грабли (не наступать снова)
- **Einsum в Unity Inference Engine сломан на шлеме**: на ПК ONNX работает, на Quest выдаёт мусор. export_model.py
  заменяет Einsum на MatMul (`replace_einsum`).
- **Не кешировать ответы Gemini по позиции.** Был баг: поставили бутылку на место конфеты и получили ответ про конфету.
- YOLO выполняется по частям (`ScheduleIterable`) с адаптивным числом слоёв на кадр. Бюджет считается по **доле**
  дропнутых кадров, минимум 16 слоёв. Если реагировать на любой дроп, бюджет застревает на 8 слоях (~750 мс).
- Dynamic resolution OVRManager отключена: под нагрузкой GPU картинка становится мыльной.
- `OVRPlugin.gpuUtilLevel` на этом шлеме всегда 0.
- Субтитры привязаны к повороту головы (пружина на кватернионе). LookRotation с world-up крутился при взгляде вниз.
- sherpa-onnx сам ставит 8 кГц, но с микрофона передаётся 16000.
- Gemini: на бесплатном тарифе `google_search` даёт 429, поэтому клиент сам отключает поиск. Модель
  gemini-3.5-flash-lite, при 5xx — фолбэк gemini-3.8-flash.
- Horizon OS игнорирует `ACTION_WIFI_ADD_NETWORKS`, поэтому используется `addNetworkSuggestions` (не протестировано).
- В PowerShell 5 `Get-Content -Raw` портит не-ASCII в UTF-8 без BOM (`·`, `…`). Такие `.cs` правь инструментом Edit.
- `git lfs migrate` оставлял pointer-файлы в рабочем дереве, лечится `git lfs checkout`.
- Quest USB-host с CH9102 не работает, поэтому связь со стиком по BLE (Nordic UART + HID-клавиатура для ноутбука).
- На ноутбуке iGPU, Quest Link ненадёжен, тестировать через сборку на шлем. Если сборка падает странно, проверь свободное место на C:.

## Секреты
- Ключ Gemini лежит **вне репозитория** и закидывается на шлем через adb в
  `/sdcard/Android/data/com.newvision.app/files/gemini_api_key.txt`. **Никогда не просить пользователя вставить ключ в чат.**

## Протокол стика (BLE / USB serial, текстовый)
`NV?`, `STATUS`, `CMD explorer|close`, `SET vol|bri|scr|theme|fx`, `BEEP`, `REBOOT`, `OFF`, `TIME`, `LOG <text>`.

## Возможные следующие шаги
- Тест на улице (дистанция по размеру объекта, boundary suppression).
- Доделать встроенный браузер: прокрутка, offscreen pre-raster, системная клавиатура.
- Компаньон: летает по комнате, сидит на краю кровати и болтает ногами (в шейдере уже есть `uSit`).
- Данные сцены MRUK для дверей и окон, дообучение на своих кадрах.
- Прокси для ключа на Cloudflare Worker, голосовые вопросы и TTS.

## Облачные сессии
В облаке нет шлема, стика, Unity и ключей. Там только правки кода, ревью, документация и планы.
Всё, что нужно проверить на устройстве, явно передавай пользователю на тест.
