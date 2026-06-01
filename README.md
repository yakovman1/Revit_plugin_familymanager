# FamilyMang — Revit Plugin

Плагин для Autodesk Revit для двустороннего обмена семействами (`.rfa`) с облачным бэкендом (FastAPI + S3/MinIO):

- **Каталог** — просмотр и скачивание семейств из хранилища в проект Revit
- **Загрузка семейства** — выгрузка семейства из проекта Revit в хранилище с автоматическим извлечением параметров и типоразмеров

## Возможности

- Вкладка **FamilyMang** на ленте Revit с панелью **Инструменты**
- Кнопка **Каталог** — просмотр семейств из бэкенда
  - Подключение к серверу по URL и **Company ID**
  - Windows-пользователь подставляется автоматически
  - Таблица семейств: имя, категория, файл, статус, размер
  - Пагинация (по 20 записей)
  - Кнопка **Загрузить в проект** — скачивает `.rfa` и загружает семейство в текущий документ Revit
- Кнопка **Загрузка семейства** — выгрузка семейства из проекта в хранилище
  - Список всех загружаемых семейств из текущего документа Revit
  - Автоматическое извлечение параметров (`FamilyParameter`) и типоразмеров (`FamilyType`)
  - Сохранение `.rfa` → вычисление SHA256 → загрузка в S3 → отправка метаданных в БД
  - Полный 4-шаговый upload flow: `init-upload` → S3 PUT → `metadata` → `complete`
- Кнопка **О плагине** — информационный диалог
- Настройки подключения сохраняются между сессиями (`%AppData%\FamilyMang\settings.json`)
- Аутентификация через **Company ID** + Windows-логин → `POST /api/v1/auth` (JWT автоматически, без ручного токена)

## Требования

| Компонент | Версия |
|-----------|--------|
| Autodesk Revit | 2022 (или совместимая) |
| .NET Framework | 4.8 |
| Visual Studio | 2019 / 2022 |
| Бэкенд | FastAPI-сервер из проекта `db_for_company/backet` |

## Структура проекта

```
FamilyMang/
├── FamilyMang.slnx              # Solution файл
├── README.md                     # Этот файл
└── FamilyMang/                   # Проект плагина
    ├── FamilyMang.csproj         # Файл проекта
    ├── FamilyMang.addin          # Манифест для Revit
    ├── App.cs                    # IExternalApplication — вкладка, панель, кнопки
    ├── CatalogCommand.cs         # IExternalCommand — команда кнопки «Каталог»
    ├── CatalogWindow.cs          # WPF-окно каталога (code-only, без XAML)
    ├── ApiClient.cs              # HTTP-клиент к бэкенду (Bearer JWT)
    ├── JwtAuthService.cs         # POST /api/v1/auth, кеш токена
    ├── Models.cs                 # DTO-классы для ответов API
    ├── Settings.cs               # Сохранение/загрузка настроек подключения
    ├── FamilyLoader.cs           # Скачивание .rfa и загрузка в документ Revit
    ├── UploadCommand.cs          # IExternalCommand — команда кнопки «Загрузка семейства»
    ├── UploadWindow.cs           # WPF-окно выбора семейства для загрузки
    ├── FamilyExtractor.cs        # Извлечение параметров/типов из Family + сохранение .rfa
    ├── Command.cs                # Команда «О плагине»
    └── Properties/
        └── AssemblyInfo.cs
```

## Сборка

### 1. Откройте проект

Откройте `FamilyMang.slnx` в Visual Studio 2019/2022.

### 2. Проверьте пути к Revit API

В файле `FamilyMang.csproj` указаны пути к библиотекам Revit:

```xml
<Reference Include="RevitAPI">
  <HintPath>D:\Program files\Revit.2022\Revit 2022\RevitAPI.dll</HintPath>
</Reference>
<Reference Include="RevitAPIUI">
  <HintPath>D:\Program files\Revit.2022\Revit 2022\RevitAPIUI.dll</HintPath>
</Reference>
```

Если Revit установлен в другую папку, измените `HintPath` на актуальный путь. Файлы `RevitAPI.dll` и `RevitAPIUI.dll` находятся в корневой папке установки Revit.

### 3. Соберите проект

```
Build → Build Solution (Ctrl+Shift+B)
```

Конфигурация: **Debug**, платформа: **x64**.

Результат сборки: `FamilyMang\bin\Debug\FamilyMang.dll`

## Установка в Revit

> **Важно:** DLL лежит в `bin\Debug\FamilyMang.dll`, **не** в `obj\Debug\Assets\`.  
> В `.addin` в теге `<Assembly>` нужен **полный путь** к DLL (не пусто и не только имя файла).

### Вариант A (рекомендуется): скрипт

Из папки `Revit_plugin_familymanager` в PowerShell:

```powershell
.\Install-RevitAddin.ps1
```

Скрипт копирует `FamilyMang.dll`, `Assets\AtpTlpLogo.png` и создаёт `FamilyMang.addin` в  
`%AppData%\Autodesk\Revit\Addins\2022\` с корректным полным путём. Перезапустите Revit.

### Вариант B: Ручная установка

1. Скопируйте файл `FamilyMang.dll` (из `bin\Debug\`) в удобную папку, например:

   ```
   C:\RevitPlugins\FamilyMang\FamilyMang.dll
   ```

   Рядом с DLL создайте папку `Assets` и скопируйте логотип (для шапки окон):

   ```
   C:\RevitPlugins\FamilyMang\Assets\AtpTlpLogo.png
   ```

   (из `FamilyMang\Assets\` в проекте или из `bin\Debug\Assets\` после сборки)

2. Скопируйте файл `FamilyMang.addin` в папку Addins Revit:

   ```
   %AppData%\Autodesk\Revit\Addins\2022\
   ```

   Полный путь обычно:
   ```
   C:\Users\<ИМЯ_ПОЛЬЗОВАТЕЛЯ>\AppData\Roaming\Autodesk\Revit\Addins\2022\
   ```

3. Скопируйте `FamilyMang.addin` в `%AppData%\Autodesk\Revit\Addins\2022\` и укажите **полный** путь к DLL:

   ```xml
   <Assembly>C:\RevitPlugins\FamilyMang\FamilyMang.dll</Assembly>
   ```

   Если `<Assembly>FamilyMang.dll</Assembly>` без пути — Revit часто **не загружает** плагин (в менеджере add-in путь пустой).

### Вариант B: Автоматическая установка через Post-Build

Добавьте в свойствах проекта (Properties → Build Events → Post-build event):

```bat
copy "$(TargetPath)" "%AppData%\Autodesk\Revit\Addins\2022\FamilyMang.dll"
copy "$(ProjectDir)FamilyMang.addin" "%AppData%\Autodesk\Revit\Addins\2022\FamilyMang.addin"
```

При этом в `.addin` путь к Assembly должен быть:
```xml
<Assembly>FamilyMang.dll</Assembly>
```

## Запуск

1. **Запустите бэкенд** (см. проект `db_for_company/backet`):

   ```bash
   cd db_for_company/backet
   docker-compose up --build
   ```

   Сервер будет доступен по адресу `http://localhost:8000`.

2. **Убедитесь, что компания зарегистрирована** в backend (`atptlp_info.companies`) и при необходимости пользователь в whitelist (`company_users`).

3. **Запустите Revit 2022**.

4. На ленте появится вкладка **FamilyMang** → панель **Инструменты**.

5. Нажмите кнопку **Каталог**.

6. В открывшемся окне:
   - **Сервер**: `http://localhost:8000` (или адрес вашего сервера)
   - **Company ID**: код компании от администратора
   - Нажмите **Загрузить список**

7. Выберите семейство из таблицы и нажмите **Загрузить в проект**.

8. Семейство будет скачано с сервера и загружено в текущий документ Revit.

### Загрузка семейства из проекта в хранилище

1. Откройте проект Revit, содержащий нужные семейства.

2. На вкладке **FamilyMang** нажмите кнопку **Загрузка семейства**.

3. В открывшемся окне:
   - Заполните **Сервер** и **Company ID** (сохраняются автоматически)
   - Выберите семейство из списка
   - Нажмите **Загрузить в хранилище**

4. Плагин автоматически:
   - Откроет семейство через `EditFamily` и сохранит как `.rfa`
   - Извлечёт все параметры (имя, тип, shared GUID, storage type, spec)
   - Извлечёт все типоразмеры с их значениями параметров
   - Вычислит SHA256 и размер файла
   - Выполнит 4-шаговую загрузку: init → S3 upload → metadata → complete

5. По завершении отобразится результат с ID загруженного семейства.

## Как это работает

### Скачивание из каталога

```
Revit UI                          Backend (FastAPI)              S3 / MinIO
────────                          ────────────────              ──────────
  │                                     │                          │
  │  GET /projects/{id}/families        │                          │
  │────────────────────────────────────>│                          │
  │  { items: [...], total: N }         │                          │
  │<────────────────────────────────────│                          │
  │                                     │                          │
  │  GET /families/{id}/download-url    │                          │
  │────────────────────────────────────>│                          │
  │  { presigned_get_url: "..." }       │                          │
  │<────────────────────────────────────│                          │
  │                                     │                          │
  │  GET presigned_get_url              │                          │
  │────────────────────────────────────────────────────────────────>│
  │  ← .rfa file bytes                 │                          │
  │<───────────────────────────────────────────────────────────────│
  │                                     │                          │
  │  Document.LoadFamily(path)          │                          │
  │  ✓ Семейство в проекте             │                          │
```

### Загрузка семейства в хранилище

```
Revit UI                          Backend (FastAPI)              S3 / MinIO
────────                          ────────────────              ──────────
  │  EditFamily → SaveAs .rfa          │                          │
  │  Extract params & types            │                          │
  │  SHA256 + size                     │                          │
  │                                     │                          │
  │  POST /families/init-upload         │                          │
  │────────────────────────────────────>│                          │
  │  { family_id, presigned_put_url }   │                          │
  │<────────────────────────────────────│                          │
  │                                     │                          │
  │  PUT presigned_put_url (.rfa)       │                          │
  │────────────────────────────────────────────────────────────────>│
  │  ← 200 OK + ETag                   │                          │
  │<───────────────────────────────────────────────────────────────│
  │                                     │                          │
  │  POST /families/{id}/metadata       │                          │
  │  { family_name, category,           │                          │
  │    parameters[], types[] }          │                          │
  │────────────────────────────────────>│  → Postgres              │
  │  { ok: true }                       │                          │
  │<────────────────────────────────────│                          │
  │                                     │                          │
  │  POST /families/{id}/complete       │                          │
  │────────────────────────────────────>│                          │
  │  { ok: true }                       │                          │
  │<────────────────────────────────────│                          │
  │                                     │                          │
  │  ✓ Семейство в хранилище           │                          │
```

## Настройки

Настройки подключения автоматически сохраняются в:

```
%AppData%\FamilyMang\settings.json
```

Формат:
```json
{
  "ServerUrl": "http://localhost:8000",
  "CompanyId": "MY_COMPANY"
}
```

## Устранение неполадок

| Проблема | Решение |
|----------|---------|
| Вкладка не появляется в Revit | Проверьте, что `.addin` файл лежит в `%AppData%\Autodesk\Revit\Addins\2022\` и путь к DLL в нём корректен |
| Ошибка «Could not load file or assembly» | Проверьте, что DLL собрана под **x64** и **.NET Framework 4.8** |
| Ошибка подключения к серверу | Убедитесь, что бэкенд запущен и доступен по указанному URL |
| 401 Unauthorized | Проверьте Company ID и что пользователь Windows в whitelist компании |
| 403 Forbidden | Windows-пользователь не разрешён для этой компании |
| Семейство не загружается в проект | Убедитесь, что в Revit открыт документ (не стартовая страница). Файл должен быть `.rfa` |
