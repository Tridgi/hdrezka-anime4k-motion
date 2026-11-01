# Changelog

## [Unreleased]

Планируемые изменения для следующего стабильного релиза (публикуются после проверки и слияния в `main`):

- Адаптивная верхняя и нижняя панели плеера; меню ✨ Anime4K/плавности и ⚙ настроек воспроизведения.
- Статистика воспроизведения, выбор скорости, кнопка пропуска опенинга; исправления DPI масштабирования.
- Выбор стабильного и тестового каналов обновлений; SHA-256, резервная копия, автоматический перезапуск.
- Исправление закрытия WinUI при обновлении и CI-проверка замены файлов.
- Копирование информации о текущей сборке; исправленные ссылки поддержки проекта.
- Исправление обрезания процентов в окне рейтинга озвучек.
- История версий внутри приложения с описанием опубликованных релизов.
- Сохранение выбранной плавности при смене качества/CDN: проверка видеофильтра при открытии нового потока; диагностика фактического подключения и частоты FPS.
- Перенос ✨ и ⚙ из верхней/левой части плеера в нижний правый блок рядом с полноэкранным режимом; улучшение компактной компоновки.
- Не отключать плавность глобально из-за обычных ошибок видеоссылок; при подтверждённом сбое SVPflow временно отключать только проблемный поток.
- При отсутствии FPS в метаданных контейнера использовать корректную частоту VideoNode, если она известна; неизвестную частоту не угадывать.

## v2.0.0

First cleaned public release of HDREZKA Anime4K Motion, based on HDREZKA commit a4676233114dbc0af1221826ae8440d9293226bb.

- Embedded Direct3D 11 libmpv rendering and Anime4K A/B/C/Light.
- Independent OpenCL motion interpolation targets: 60, 120, experimental 165 FPS.
- Configurable opening skip and inactivity cursor/control hiding.
- Fixed delayed access violation after disabling FPS by pinning worker runtime modules for the process lifetime.
- Hidden accidental global keyboard accelerator tooltips over the video.
- Separate clean app data folder; project-specific update link; no bundled personal state.

Earlier v1/v2/v2.1 names referred to local development builds; they are not separate public releases.
