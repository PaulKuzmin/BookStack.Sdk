<#
.SYNOPSIS
    Выкладывает markdown-документы из AltWayDocs на портал BookStack.

.DESCRIPTION
    Обёртка над tools/BookStackDocsPush. Каталог становится книгой, подкаталог первого уровня —
    главой, файл — страницей. Перекрёстные ссылки между документами переписываются на адреса
    портала.

    ФАЙЛЫ НА ДИСКЕ НЕ МЕНЯЮТСЯ: хранилище только читается.

    Токен нужен один, половинками (профиль → API Tokens на портале-приёмнике). Писать его в
    постоянные переменные среды не нужно: заведите хранилище рядом со скриптами —

        ./set-tokens.ps1 -Portal To

    Значения шифруются средствами Windows на связку «эта учётная запись + эта машина» и попадают
    в окружение только на время запуска. Переносимый вариант — файл tokens.env по образцу
    tokens.env.example (простой текст, лежит в .gitignore). Оба файла можно указать явно ключом
    -TokenFile. Переменная, заданная руками в этом окне, имеет приоритет над файлом.

    Права токену: заводить и править книги, главы, страницы и полки; для -Prune ещё и удалять
    страницы. Для -DryRun токен не нужен вовсе.

.PARAMETER TokenFile
    Взять токены из этого файла вместо tokens.secret.xml / tokens.env рядом со скриптом.
    Расширение .xml означает зашифрованное хранилище, любое другое — простой текст ИМЯ=значение.
.PARAMETER To
    Портал-приёмник. По умолчанию стенд: на боевой портал попасть можно только назвав его явно.

.PARAMETER Books
    Каталоги и книги в виде «путь=название». Путь считается от корня хранилища.

.PARAMETER Shelf
    Полка, на которую складываются книги. Пусто — полка не трогается вовсе.
    Уже стоящие на полке книги остаются.

.PARAMETER DryRun
    Прочитать диск, показать раскладку и судьбу ссылок. На портал не ходит, токен не требуется.

.PARAMETER Prune
    Снести на портале страницы, чей файл исчез. По умолчанию они только перечисляются.
    Переименованный файл сюда не попадает: его страница узнаётся по заголовку и переезжает.

.PARAMETER ExpectInstance
    Работать, только если instance_id портала совпал. Замок от выкладки сотни страниц не туда.

.EXAMPLE
    ./push-docs.ps1 -DryRun

.EXAMPLE
    ./push-docs.ps1

.EXAMPLE
    ./push-docs.ps1 -To https://help.altway.pro -ExpectInstance "<id боевого портала>"
#>
# PositionalBinding=$false обязателен. Без него первый же аргумент без имени — например набранный
# по привычке --dry-run — сядет на первый параметр ($To), молча подменив адрес портала, а сам ключ
# до инструмента не доедет.
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $To = 'https://test.help.altway.pro',
    [string] $Docs = 'F:\AltWay\AltWayDocs',
    [string[]] $Books = @(
        'Архитектура=Архитектура',
        'Будущее=Будущее',
        'Инструкции/Единый-вход=Единый вход',
        'Инструкции/Клиент-WPF=Клиент-WPF'
    ),
    [string] $Shelf = 'Документация проекта',
    [switch] $NoShelf,
    [string] $ExpectInstance,
    [switch] $DryRun,
    [switch] $Prune,
    [switch] $AllowProduction,
    [string] $TokenFile,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Extra
)

$scriptRoot = $PSScriptRoot
$previousEncoding = [Console]::OutputEncoding

# Вывод инструмента идёт в UTF-8; без этой строки русский текст в консоли превращается в кашу.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Отказ идёт в поток ошибок и без стека вызовов: человеку тут нужен текст, а не место в скрипте,
# где он родился, а перенаправление 2> должно этот текст ловить.
function Fail([string] $Message) {
    [Console]::Error.WriteLine($Message)
    [Console]::OutputEncoding = $previousEncoding
    exit 1
}

# Токены берутся из файла рядом со скриптами и кладутся в окружение ТОЛЬКО этого процесса: ни в
# реестр, ни в постоянное окружение пользователя ничего не пишется. Уже заданная переменная имеет
# приоритет, поэтому файл не перебивает то, что задали руками в этом окне.
. (Join-Path $scriptRoot 'bookstack-tokens.ps1')
$tokenSource = Import-BookStackTokens -Root $scriptRoot -TokenFile $TokenFile
if ($tokenSource) { Write-Host $tokenSource -ForegroundColor DarkGray }

# Ключ инструмента, набранный по привычке в стиле --dry-run, попадает в хвост. Для инструмента это
# уже безразлично, а вот собственные решения скрипта (спрашивать ли токен, пугать ли боевым
# порталом) должны считать такой прогон сухим, иначе отказ будет про то, чего не требуется.
if ($Extra -contains '--dry-run') { $DryRun = $true }

# Хвостовая косая черта в пути со пробелом ломает разбор командной строки: кавычка перед ней
# перестаёт закрывать значение, и следующие ключи уезжают внутрь пути.
$Docs = $Docs.TrimEnd('\', '/')

if ([string]::IsNullOrWhiteSpace($To)) { Fail "Пустой -To: некуда выкладывать." }
if ([string]::IsNullOrWhiteSpace($Docs)) { Fail "Пустой -Docs: нечего выкладывать." }

if (-not (Test-Path -LiteralPath $Docs)) {
    Fail "Нет каталога с документами: $Docs. Укажите свой путь ключом -Docs."
}

# Замок от выкладки сотен страниц на боевой портал по невнимательности.
if (-not $DryRun -and $To -notmatch 'test\.' -and -not $AllowProduction) {
    Fail @"
Это не стенд: $To

Выкладка на боевой портал требует явного согласия. Если так и задумано, добавьте -AllowProduction
и задайте замок -ExpectInstance <id портала>, чтобы промах адресом остановился до первой записи.
$(if ($Prune) { "И помните: с -Prune страницы, чей файл исчез, будут снесены." })
"@
}

# Токен нужен только на настоящий прогон: сухой читает диск и на портал не ходит.
if (-not $DryRun) {
    $haveOwn = -not [string]::IsNullOrWhiteSpace($env:BOOKSTACK_TOKEN_ID) -and
               -not [string]::IsNullOrWhiteSpace($env:BOOKSTACK_TOKEN_SECRET)
    $haveTo = -not [string]::IsNullOrWhiteSpace($env:BOOKSTACK_TO_TOKEN_ID) -and
              -not [string]::IsNullOrWhiteSpace($env:BOOKSTACK_TO_TOKEN_SECRET)

    if (-not $haveOwn -and -not $haveTo) {
        Fail @"
Не задан токен портала $To.

Выпустить: $To → аватар → Edit Profile → API Tokens → Create Token.
Задать в этом окне:
    `$env:BOOKSTACK_TOKEN_ID     = "<id>"
    `$env:BOOKSTACK_TOKEN_SECRET = "<секрет>"

Половинки местами не менять: на перестановку BookStack отвечает тем же 401, что и на чужой токен.

Токены можно не задавать руками: заведите хранилище рядом со скриптами — ./set-tokens.ps1
(значения шифруются на эту учётную запись и эту машину), либо файл tokens.env по образцу
tokens.env.example.
Посмотреть раскладку без токена вовсе: ./push-docs.ps1 -DryRun
"@
    }
}

# Хвост идёт ПЕРВЫМ: разбор в инструменте последний-побеждает, поэтому смуглённый в хвосте --to
# или --docs перебивался бы нашими проверенными значениями, а не наоборот.
$toolArgs = @()
if ($Extra) { $toolArgs += $Extra }

$toolArgs += @('--to', $To, '--docs', $Docs)
foreach ($book in $Books) { $toolArgs += @('--book', $book) }

if (-not $NoShelf -and -not [string]::IsNullOrWhiteSpace($Shelf)) { $toolArgs += @('--shelf', $Shelf) }
if (-not [string]::IsNullOrWhiteSpace($ExpectInstance)) { $toolArgs += @('--expect-instance', $ExpectInstance) }
if ($DryRun) { $toolArgs += '--dry-run' }
if ($Prune) { $toolArgs += '--prune' }
if ($VerbosePreference -ne 'SilentlyContinue') { $toolArgs += '--verbose' }

Write-Host "Выкладка документов: $Docs → $To" -ForegroundColor Cyan
if ($Prune -and -not $DryRun) {
    Write-Host "-Prune: страницы, чей файл исчез, будут снесены в корзину." -ForegroundColor Yellow
}

# Сообщения инструмента идут в поток ошибок; при 'Stop' слитые потоки превратили бы их в обрыв
# скрипта, и код выхода инструмента до вызывающего не доехал бы.
$ErrorActionPreference = 'Continue'

# Каталог проекта берётся от скрипта, а не через смену текущего: чужую сессию двигать незачем.
& dotnet run --project (Join-Path $scriptRoot 'tools/BookStackDocsPush') -- @toolArgs
$code = $LASTEXITCODE

[Console]::OutputEncoding = $previousEncoding
exit $code
