# New Vision

Приложение для Meta Quest 3: показывает окружение через passthrough и обводит предметы рамками с подписями.
Основано на примере Meta `Unity-PassthroughCameraApiSamples`, сцена `MultiObjectDetection`.

## Управление в шлеме

| Кнопка | Действие |
|---|---|
| Y или ☰ | меню (модель, выбор предметов) |
| Стик | перемещение по меню (влево/вправо: на 10 строк) |
| A / курок | переключить строку в меню; вне меню: поставить 3D-метки на найденные предметы |
| B | закрыть меню; вне меню: убрать метки |

## Модели

Все модели из папки `Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Model/*.onnx`
(кроме исходной `yolov9onnx.onnx`) доступны в меню, строка **Model**. Выбор запоминается по имени модели.
По умолчанию используется `yolo11n_416`.

Замеры на Quest 3 (GPUCompute, с letterbox):

| Модель | Время на кадр |
|---|---|
| yolo11n_320 | ~89 мс |
| yolo11n_416 | ~96 мс |
| yolo11s_320 | ~167 мс |
| yolo11s_416 | ~193 мс |

### Добавить модель

1. Экспорт (нужен Python с `ultralytics`, у нас: `C:\New_Vision\tools\venv`):
   ```
   C:\New_Vision\tools\venv\Scripts\python.exe Tools\export_model.py yolo11m 416
   ```
   Своя дообученная модель: `Tools\export_model.py path\to\best.pt 416 my_model_416`.
2. Unity: **New Vision → Convert Models**. Модели конвертируются в `.sentis` (веса Float16) и попадают в меню.
3. Build And Run.

Модель должна находить те же классы в том же порядке, что и `SentisYoloClasses.txt`.
Для модели со своими классами нужен свой файл подписей и таблица размеров (`ObjectSizes.cs`).

## Прочее

- **New Vision → Apply App Settings**: название, идентификатор `com.newvision.app`, иконка, разрешение скрывать границу.
- Иконка рисуется скриптом `Tools/make_icon.py`.
- Время инференса пишется в лог: `adb logcat -s Unity | findstr NewVision`.
