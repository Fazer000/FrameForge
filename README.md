# Frame Forge

![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D6?style=flat-square)
![Framework](https://img.shields.io/badge/Framework-.NET%2010-512BD4?style=flat-square)
![Language](https://img.shields.io/badge/Language-C%23-239120?style=flat-square)
![UI](https://img.shields.io/badge/UI-WPF-6B7280?style=flat-square)


---

## 🖥️ О приложении

Frame Forge повышает качество аниме из библиотеки **AnimeLib Desktop** и обычных видео с помощью моделей Real-CUGAN и Real-ESRGAN. Обработка выполняется ресурсами вашей видеокарты.

---

## ✨ Основные возможности

- 🎞️ **Обработка видео** — целиком или по фрагментам до 300 секунд.
- 📐 **Разрешение** — 1080p · FHD, 1440p · QHD, 2160p · 4K или свой размер. Кадр вписывается в заданные границы с сохранением пропорций.
- 🎛️ **Кодирование H.264** — компактное (CRF 22, по умолчанию), сбалансированное (CRF 20) или высокое качество (CRF 16).
- 📋 **Очередь** — обработка по одному заданию, общий прогресс и оценка оставшегося времени. Параметры отдельного задания можно изменить после добавления. Очередь сохраняется после перезапуска.
- 🔊 **Дорожки и вложения** — для обычных файлов исходное видео остаётся на месте. В итоговый MKV переносятся аудиодорожки, встроенные субтитры и вложения без перекодирования.
- 📚 **Интеграция с AnimeLib Desktop** — Frame Forge может заменить проверенным результатом серию в локальной библиотеке. В совместимой версии AnimeLib Desktop (2.1.0) у серии отображаются фактическое разрешение и метка `(FF)`.

### Алгоритмы

| Профиль | Когда выбрать                                                                                                                   |
| --- |---------------------------------------------------------------------------------------------------------------------------------|
| Кино · Высокое · Real-ESRGAN Plus | Для фильмов, сериалов и домашних видео с реальной съёмкой. Подойдёт, если хочется сделать лица, предметы и мелкие детали чётче. |
| Аниме · Высокое · Real-CUGAN Pro | Для аккуратного улучшения аниме. Сочетает в себе мягкое шумоподавление и высокую четкость линий.                                |
| Аниме · Высокое · Real-CUGAN + шумоподавление | Для старых или сильно сжатых серий, где заметны рябь, шум или квадраты от сжатия. Мелкие детали при очистке могут стать мягче.  |
| Аниме · Высокое · Real-CUGAN | Для аниме без заметного шума, когда хочется подчеркнуть линии и контуры персонажей без шумоподавления.                          |
| Аниме · Базовое · Real-ESRGAN Video v3 | Для лёгкого повышения чёткости изображения. Обрабатывает картинку лучше штатного инструмета AnimeLib Desktop.                   |

С Real-CUGAN Pro для видео 720p доступны 1440p и 4K. С Real-CUGAN с шумоподавлением из 720p можно получить до 1440p.

Если модель выдаёт кадр больше целевого, Frame Forge уменьшает его через Lanczos. Промежуточное видео без потерь в этом режиме может занимать много места.

---

## 🛠️ Технические характеристики

### Требования для запуска приложения

- Windows x64.
- Процессор с поддержкой AVX2.
- Видеокарта с драйвером, поддерживающим Vulkan.

Установщик включает среду .NET, Video2X с моделями, FFmpeg, ffprobe и библиотеки Visual C++.

### Требования для разработки

- Windows x64 и .NET 10 SDK. Решение можно открыть в Rider.
- Video2X, FFmpeg, ffprobe и видеокарта с Vulkan для локальной обработки и сквозного теста.
- Inno Setup 6 и Visual Studio или Build Tools с компонентом C++ x64 для сборки установщика.

### Используемый стек

- **Интерфейс** — C#, .NET 10 и WPF.
- **Обработка видео** — Video2X, Real-CUGAN и Real-ESRGAN на Vulkan.
- **Кодирование и проверка файлов** — FFmpeg, ffprobe и H.264 на CPU.
- **Установщик и тесты** — Inno Setup и xUnit.

---

## 🎬 Работа с очередью

1. Выберите алгоритм, разрешение, качество кодирования и папку результатов. Для пробы снимите флажок **«Обрабатывать видео целиком»** и задайте начало и длину фрагмента.
2. Нажмите **«Добавить видео…»** и выберите файлы.
3. Чтобы изменить параметры отдельного задания, выделите его и нажмите **«Изменить…»**. Редактировать можно ожидающие, отменённые, прерванные и завершившиеся ошибкой задания. Выбранное качество отображается в колонке **«Кодирование»** в виде `CRF 20`.
4. Нажмите **«Запустить очередь»**. Остановка отменяет текущую обработку и сохраняет ожидающие задания. Кнопка **«Повторить»** возвращает незавершённые задания в очередь. Кнопка **«Убрать»** удаляет записи из очереди, файлы остаются на диске.

### Библиотека AnimeLib Desktop

1. Нажмите **«Из AnimeLib…»** и выберите скачанные серии. Путь можно задать в разделе **«Библиотека AnimeLib»**. Серии обрабатываются целиком. Импорт локальных HLS-плейлистов пока не поддерживается.
2. Закройте AnimeLib Desktop перед запуском очереди. Frame Forge готовит и проверяет совместимый MP4 перед заменой. Если MP4 не позволяет сохранить исходные дорожки, например встроенные субтитры или звук в кодеке, отличном от AAC, серия остаётся в библиотеке, а улучшенный MKV доступен через **«Результат»**.
3. После успешной замены временный оригинал удаляется. Если финальная проверка не проходит, Frame Forge пытается восстановить оригинал. При невозможности отката копия остаётся в `.frameforge`.

---

## ⚙️ Файлы и настройки

| Что хранится | Где искать |
| --- | --- |
| Настройки и очередь | `%LOCALAPPDATA%\FrameForge\workspace.json` |
| Результаты обычных видео | По умолчанию **Видео → Frame Forge**. В папке задания находятся `enhanced.mkv`, `processing.log` и `report.json`. |
| Служебные данные библиотеки AnimeLib | `.frameforge` внутри каталога библиотеки. Здесь хранятся рабочие файлы, журналы, отчёты и отметки об улучшении. |

После успешной замены серии Frame Forge удаляет рабочий MKV. Чтобы сохранить его, включите **«Сохранять временные файлы»**. При ошибке откройте папку задания кнопкой **«Результат»** и проверьте `processing.log` и `report.json`, если отчёт сохранился.

---

## 🚀 Сборка приложения

1. Клонируйте репозиторий и откройте его каталог.

   ```powershell
   git clone https://github.com/Fazer000/FrameForge.git
   cd FrameForge
   ```

2. Распакуйте [Video2X 6.4.0 CLI для Windows x64](https://github.com/k4yt3x/video2x/releases/tag/6.4.0) в `tools/video2x/`, а FFmpeg 9.0.2 Essentials — в `tools/ffmpeg/ffmpeg-9.0.2-essentials_build/`. Для сборки Frame Forge с исправлением стыков Real-CUGAN и ускорением обработки целого кадра замените штатную `tools/video2x/libvideo2x.dll` на проверенную DLL Frame Forge. Возьмите её из рабочей копии, где уже установлены эти правки. Папка `tools/` не входит в Git.

   SHA-256 проверенной DLL для текущей сборки можно сверить командой `Get-FileHash tools/video2x/libvideo2x.dll`.

   ```text
   E51DB2541589C2022CA7564A7D57B11B3CD05BF2786C92CF73C72E96E6BD2C4C
   ```

3. Соберите и запустите приложение.

   ```powershell
   dotnet build frame-forge.sln -c Release
   dotnet run --project src/FrameForge -c Release --no-build
   ```

4. Для сборки установщика подставьте нужную версию вместо `1.0.0` и выполните команду в Windows PowerShell 5.1 или PowerShell 7.

   ```powershell
   .\installer\Build-Installer.ps1 -Version 1.0.0
   ```

   Готовый файл появится в `artifacts/installer/FrameForge-Setup.exe`. Если `ISCC.exe` находится в нестандартной папке, укажите его путь параметром `-Compiler`.

### Вспомогательные команды

Обычные тесты не требуют GPU.

```powershell
dotnet test frame-forge.sln -c Release
```

Чтобы запустить сквозной GPU-тест и измерить покрытие ядра, выполните команды из корня репозитория с подготовленной папкой `tools/`.

```powershell
$env:FRAMEFORGE_TEST_ROOT = (Get-Location).Path
dotnet test frame-forge.sln -c Release --settings coverage.runsettings --collect 'XPlat Code Coverage' --results-directory experiments/test-results
```

Сквозной тест создаёт синтетическое видео и временную библиотеку AnimeLib в `experiments/`. Отчёт Cobertura показывает покрытие строк и ветвей `FrameForge.Core`. Цель по покрытию — 80%.

---

## 📁 Структура проекта

```text
src/
├── FrameForge/          # Интерфейс WPF
│   ├── Jobs/            # Общая панель настроек и редактор задания
│   └── Theme/           # Палитра и стили
└── FrameForge.Core/
    ├── Processing/     # Профили, разрешение и обработка видео
    ├── Queue/          # Задания, выполнение и расчёт прогресса
    ├── AnimeLib/       # Каталог, замена серий и восстановление
    ├── Storage/        # Сохранение очереди и настроек
    └── Tools/          # Запуск внешних инструментов и чтение метаданных
tests/
├── FrameForge.Tests/   # Тесты xUnit
└── FrameForge.Smoke/   # Сквозные проверки с реальными инструментами
installer/              # Сборка установщика и сведения о зависимостях
```

---

## 📥 Скачать и установить

| Приложение | Последний релиз | Ссылка |
| --- | --- | --- |
| 🖥️ **Frame Forge (Windows)** | [![Frame Forge Release](https://img.shields.io/github/v/release/Fazer000/FrameForge?label=frame-forge&style=flat-square)](https://github.com/Fazer000/FrameForge/releases) | [Скачать Frame Forge](https://github.com/Fazer000/FrameForge/releases) |
| 🖥️ **AnimeLib Desktop (Windows, Linux)** | [![Desktop Release](https://img.shields.io/github/v/release/Fazer000/AnimeLib-Desktop?label=desktop&style=flat-square)](https://github.com/Fazer000/AnimeLib-Desktop/releases) | [Скачать AnimeLib Desktop](https://github.com/Fazer000/AnimeLib-Desktop/releases) |
| 📱 **AnimeLib Mobile (Android)** | [![Mobile Release](https://img.shields.io/github/v/release/Fazer000/AnimeLib-Mobile?label=mobile&style=flat-square&color=ff8000)](https://github.com/Fazer000/AnimeLib-Mobile/releases) | [Скачать AnimeLib Mobile](https://github.com/Fazer000/AnimeLib-Mobile/releases) |
| 🎬 **PlayerLib (Android)** | [![Player Release](https://img.shields.io/badge/release-v1.0.0-FF7A00?style=flat-square)](#) | [Скачать PlayerLib](https://github.com/Fazer000/PlayerLib-Mobile/releases) |

---

## 📄 Сторонние компоненты

Сведения о Video2X, моделях, FFmpeg и библиотеках Visual C++ приведены в [уведомлениях о сторонних компонентах](installer/licenses/THIRD-PARTY-NOTICES.txt).
