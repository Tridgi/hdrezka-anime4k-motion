# Сборка HDREZKA Anime4K Motion 2.1 из исходников

[← README](../README.md) · [К релизу 2.1](RELEASE_2.1.md) · [Правила публикации](RELEASES.md)

## Окружение

Проект ориентирован на **Windows x64 / WinUI 3**.

- Windows 10 (19041+) или Windows 11.
- **.NET SDK 8** и PowerShell (`pwsh`, для сценариев CI).
- Доступ к NuGet и источникам закреплённых зависимостей из `scripts/bootstrap.ps1`.
- 7-Zip для архива разработки mpv (PATH или стандартная папка Program Files).
- Дополнительные компоненты SVPflow/VapourSynth/OpenCL извлекаются bootstrap-скриптом. Их не следует заменять непроверенными файлами.

Важные файлы: `Directory.Build.props` — единая версия и сборочные атрибуты; `HDREZKA.slnx` — решение; `src/HDREZKA.App` — WinUI-клиент; `src/HDREZKA.Core` — API/парсеры; `tests/HDREZKA.Tests` — тесты ядра.

## Получение кода и зависимостей

```powershell
git clone https://github.com/Tridgi/hdrezka-anime4k-motion.git
cd hdrezka-anime4k-motion
git checkout v2.1.0

pwsh -File scripts/bootstrap.ps1
```

Bootstrap загружает закреплённые архивы компонентов и сверяет **SHA-256** до распаковки. Не включайте в GitHub дистрибутивы native-зависимостей, дампы или пользовательские файлы.

## Тестирование

```powershell
dotnet restore tests/HDREZKA.Tests/HDREZKA.Tests.csproj
dotnet test tests/HDREZKA.Tests/HDREZKA.Tests.csproj -c Release

python scripts/test-motion-resolutions.py
python scripts/test-details-pages.py
pwsh -File scripts/test-updater.ps1
```

Тесты покрывают парсинг жанров и комментариев, ответы API и обработку ошибок, XAML/обработчики страниц, расчёт размеров SVPflow и сценарий обновления/резервной копии. Они не запускают реальный сайт и не доказывают работу CAPTCHA, всех DPI/видеокарт.

## Публикация portable x64

```powershell
dotnet restore src/HDREZKA.App/HDREZKA.App.csproj -p:Platform=x64 -p:SelfContained=true

dotnet publish src/HDREZKA.App/HDREZKA.App.csproj `
  -c Release -p:Platform=x64 --self-contained true -o artifacts/app

pwsh -File scripts/package.ps1 -InputDirectory artifacts/app -OutputDirectory artifacts/portable
```

Распакованные файлы из `artifacts/portable` составляют портативную Windows-версию. `runtime`, `motion`, `shaders` и `Updater/InstallUpdate.ps1` необходимы; не удаляйте их выборочно.

**ZIP для GitHub Release** упаковывается Windows CI и для 2.1.0 называется `HDREZKA-Anime4K-Motion-2.1.0-win-x64.zip`. Файл `SHA256SUMS.txt` вычисляется после создания окончательного архива.

## Версии и сборочные атрибуты

Единый источник номера версии — `Directory.Build.props`:

```xml
<Version>2.1.0</Version>
<FileVersion>2.1.0.0</FileVersion>
<AssemblyVersion>2.1.0.0</AssemblyVersion>
<InformationalVersion>2.1.0</InformationalVersion>
```

`HDREZKA.Core.Api.BuildInfo` берёт версию из атрибутов сборки. GitHub Actions передаёт commit в `SourceRevisionId`, поэтому идентификатор исходников может отображаться в диагностике рядом с номером версии. При новом релизе обновите README, CHANGELOG, имя ZIP и документацию, но **не копируйте номер версии вручную в код HTTP User-Agent**.

## Что исключать из релиза

- `%APPDATA%\HDREZKA-Anime4K-Motion`, локальные `settings.json` и cookies;
- логины, пароли, журналы, приватные скриншоты, дампы и тестовые видео;
- `.git`, промежуточные `bin`/`obj`, PDB и архивы зависимостей.

`scripts/package.ps1` исключает известные локальные и диагностические файлы. Исходные ZIP создаются на GitHub из тега отдельно от бинарного архива; pinned native assets восстанавливаются bootstrap-скриптом.

## Выпуск и повторяемость

- **`build.yml`** — тесты и переносимый Windows artifact на `main` и PR.
- **`testing-release.yml`** — prerelease в разрешённых тестовых ветках, тег `test-<sha10>`.
- **`stable-release.yml`** — стабильный выпуск по тегу `vX.Y.Z` на коммите `main`. Для одобренного запуска 2.1.0 есть специальный main-merge триггер с названием `release: v2.1.0`: тег создаётся **только после успешных тестов и упаковки**.

См. [RELEASES.md](RELEASES.md) для точного регламента и [UPDATER.md](UPDATER.md) для требований к ZIP/SHA-256.

## Локальная диагностика видео

При отладке допускается `--local-file=ABSOLUTE_PATH` с локальным тестовым файлом. Дополнительные флаги `--smooth-report=ABSOLUTE_PATH` и `--switch-report=ABSOLUTE_PATH` создают локальные отчёты по интерполяции; в обычном воспроизведении они не используются. Такие отчёты могут содержать сведения о машине — не включайте их в релиз.

## Лицензии

Соблюдайте `LICENSE.md`, `THIRD_PARTY.md` и уведомления всех сторонних компонентов. Исходная атрибуция eddinwork и voidboost должна сохраняться.

[← README](../README.md)
