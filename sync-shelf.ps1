<#
.SYNOPSIS
    Переносит полку с help.altway.pro на test.help.altway.pro.

.DESCRIPTION
    Обёртка над tools/BookStackShelfSync. Полка сама по себе не переносится — переезжают её книги
    (выгрузка архивом, импорт на приёмнике), а полка на той стороне собирается заново.

    Токены нужны ОБА, половинками, как их выдаёт BookStack (профиль → API Tokens). Писать их в
    постоянные переменные среды не нужно: заведите хранилище рядом со скриптами —

        ./set-tokens.ps1

    Значения шифруются средствами Windows на связку «эта учётная запись + эта машина» и попадают
    в окружение только на время запуска. Переносимый вариант — файл tokens.env по образцу
    tokens.env.example (простой текст, лежит в .gitignore). Оба файла можно указать явно ключом
    -TokenFile. Переменная, заданная руками в этом окне, имеет приоритет над файлом.

.PARAMETER TokenFile
    Взять токены из этого файла вместо tokens.secret.xml / tokens.env рядом со скриптом.
    Расширение .xml означает зашифрованное хранилище, любое другое — простой текст ИМЯ=значение.
.PARAMETER Shelf
    Слаг полки, последний кусок адреса /shelves/<слаг>.

.PARAMETER Replace
    Снести на приёмнике книги с тем же слагом, чтобы вместо дублей вышла замена.
    Прежняя книга уезжает в корзину ДО импорта и добивается ПОСЛЕ него: упавший импорт не оставит
    приёмник вовсе без книги.

.PARAMETER RewriteLinks
    Заменить в перенесённых страницах адрес источника на адрес приёмника. Правятся только страницы
    в markdown и только вне блоков кода; остальное перечисляется для ручной правки.

.PARAMETER DryRun
    Показать план и выйти, ничего не меняя. Начинать стоит с него.

.EXAMPLE
    ./sync-shelf.ps1 -DryRun

.EXAMPLE
    ./sync-shelf.ps1 -Replace -RewriteLinks

.EXAMPLE
    ./sync-shelf.ps1 -Shelf "drugaya-polka" -DryRun
#>
# PositionalBinding=$false обязателен. Без него первый же аргумент без имени — например набранный
# по привычке --dry-run — сядет на первый параметр ($Shelf), а сам ключ до инструмента не доедет:
# вместо показа плана начнётся настоящий перенос.
[CmdletBinding(PositionalBinding = $false)]
param(
    [string] $Shelf = 'kitaiskie-ploshhadki-i-servisy',
    [string] $From = 'https://help.altway.pro',
    [string] $To = 'https://test.help.altway.pro',
    [switch] $Replace,
    [switch] $RewriteLinks,
    [switch] $DryRun,
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

if ([string]::IsNullOrWhiteSpace($Shelf)) { Fail "Пустой -Shelf: нечего переносить." }
if ([string]::IsNullOrWhiteSpace($From)) { Fail "Пустой -From: неоткуда переносить." }
if ([string]::IsNullOrWhiteSpace($To)) { Fail "Пустой -To: некуда переносить." }

# Пишет инструмент ТОЛЬКО в -To, и промах тут необратим: с -Replace книга на приёмнике сносится и
# добивается из корзины. Своего замка по идентификатору установки у переноса полки нет, поэтому
# рубеж стоит здесь.
if ($To -notmatch 'test\.' -and -not $AllowProduction) {
    Fail @"
Приёмник не похож на стенд: $To

Пишущая сторона здесь — именно -To. Если перенос на боевой портал и правда нужен, добавьте
-AllowProduction. Проверьте заодно, не перепутаны ли -From и -To: обратный ход снесёт на
help.altway.pro то, что там уже есть.
"@
}

function Assert-Token([string] $Name, [string] $Where) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($Name))) {
        Fail @"
Не задана переменная окружения $Name (токен для $Where).

Выпустить: $Where → аватар → Edit Profile → API Tokens → Create Token.
Задать в этом окне:
    `$env:BOOKSTACK_FROM_TOKEN_ID     = "<id с $From>"
    `$env:BOOKSTACK_FROM_TOKEN_SECRET = "<секрет с $From>"
    `$env:BOOKSTACK_TO_TOKEN_ID       = "<id с $To>"
    `$env:BOOKSTACK_TO_TOKEN_SECRET   = "<секрет с $To>"

Половинки местами не менять: на перестановку BookStack отвечает тем же 401, что и на чужой токен.

Токены можно не задавать руками: заведите хранилище рядом со скриптами — ./set-tokens.ps1
(значения шифруются на эту учётную запись и эту машину), либо файл tokens.env по образцу
tokens.env.example.
"@
    }
}

Assert-Token 'BOOKSTACK_FROM_TOKEN_ID' $From
Assert-Token 'BOOKSTACK_FROM_TOKEN_SECRET' $From
Assert-Token 'BOOKSTACK_TO_TOKEN_ID' $To
Assert-Token 'BOOKSTACK_TO_TOKEN_SECRET' $To

# Хвост идёт ПЕРВЫМ: разбор в инструменте последний-побеждает, поэтому смуглённый в хвосте --to
# или --from перебивался бы нашими проверенными значениями, а не наоборот.
$toolArgs = @()
if ($Extra) { $toolArgs += $Extra }

$toolArgs += @('--from', $From, '--to', $To, '--shelf', $Shelf)

if ($Replace) { $toolArgs += '--replace' }
if ($RewriteLinks) { $toolArgs += '--rewrite-links' }
if ($DryRun) { $toolArgs += '--dry-run' }
if ($VerbosePreference -ne 'SilentlyContinue') { $toolArgs += '--verbose' }

Write-Host "Перенос полки [$Shelf]: $From → $To" -ForegroundColor Cyan

if (-not $DryRun) {
    if ($Replace) {
        Write-Host ("-Replace: книги с тем же слагом на $To будут снесены и добиты из корзины, " +
                    "то есть безвозвратно.") -ForegroundColor Yellow
    }
    else {
        Write-Host ("Без -Replace книги, уже лежащие на приёмнике, останутся, и рядом появятся дубли.") -ForegroundColor Yellow
    }

    Write-Host ("Состав полки на приёмнике замещается перенесёнными книгами: посторонние книги, " +
                "если они на ней стоят, с неё снимутся.") -ForegroundColor Yellow
    Write-Host "Архивы выгрузки останутся во временном каталоге; путь инструмент напечатает." -ForegroundColor DarkGray
}

# Сообщения инструмента идут в поток ошибок; при 'Stop' слитые потоки превратили бы их в обрыв
# скрипта, и код выхода инструмента до вызывающего не доехал бы.
$ErrorActionPreference = 'Continue'

# Каталог проекта берётся от скрипта, а не через смену текущего: чужую сессию двигать незачем.
& dotnet run --project (Join-Path $scriptRoot 'tools/BookStackShelfSync') -- @toolArgs
$code = $LASTEXITCODE

[Console]::OutputEncoding = $previousEncoding
exit $code
