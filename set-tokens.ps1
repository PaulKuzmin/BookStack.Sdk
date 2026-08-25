<#
.SYNOPSIS
    Заводит зашифрованное хранилище токенов BookStack рядом со скриптами.

.DESCRIPTION
    Спрашивает половинки токенов и кладёт их в tokens.secret.xml, зашифровав средствами Windows
    (DPAPI) на связку «эта учётная запись + эта машина». В постоянное окружение и в реестр ничего
    не пишется; файл внесён в .gitignore.

    Ввод скрытый: значения не показываются на экране и не попадают в историю команд.
    Пустой ответ означает «это оставить как было».

    Где взять: портал → аватар → Edit Profile → API Tokens → Create Token. Токен выдаётся двумя
    половинками, Token ID и Token Secret, и секрет показывается один раз.

.PARAMETER Portal
    Какие токены спрашивать:
      Both   (по умолчанию) — и боевой портал, и стенд: хватит обоим скриптам;
      From   только источник переноса полки (help.altway.pro);
      To     только приёмник: и перенос полки, и выкладка документов берут его.

.PARAMETER Show
    Ничего не спрашивать, показать, что уже лежит в хранилище (имена, без значений).

.EXAMPLE
    ./set-tokens.ps1

.EXAMPLE
    ./set-tokens.ps1 -Portal To

.EXAMPLE
    ./set-tokens.ps1 -Show
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('Both', 'From', 'To')]
    [string] $Portal = 'Both',
    [switch] $Show
)

$scriptRoot = $PSScriptRoot
. (Join-Path $scriptRoot 'bookstack-tokens.ps1')

$path = Get-BookStackSecretPath $scriptRoot

if ($Show) {
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host "Хранилища ещё нет: $path"
        Write-Host "Завести: ./set-tokens.ps1"
        exit 0
    }

    Write-Host "Хранилище: $path"
    (Import-Clixml -LiteralPath $path).Keys | Sort-Object | ForEach-Object { Write-Host "  $_" }
    Write-Host ""
    Write-Host "Значения не показываются: они зашифрованы на эту учётную запись и эту машину."
    exit 0
}

# Спрашиваются ровно те половинки, которые нужны выбранной стороне. Лишние вопросы человек
# пролистывает пустым вводом, а пролистанный вопрос — это ровно тот токен, которого потом не хватит.
$wanted = switch ($Portal) {
    'From' { @(
        @{ Name = 'BOOKSTACK_FROM_TOKEN_ID'; Hint = 'Token ID с help.altway.pro (источник переноса)' },
        @{ Name = 'BOOKSTACK_FROM_TOKEN_SECRET'; Hint = 'Token Secret с help.altway.pro' }) }
    'To' { @(
        @{ Name = 'BOOKSTACK_TO_TOKEN_ID'; Hint = 'Token ID с test.help.altway.pro (приёмник)' },
        @{ Name = 'BOOKSTACK_TO_TOKEN_SECRET'; Hint = 'Token Secret с test.help.altway.pro' }) }
    default { @(
        @{ Name = 'BOOKSTACK_FROM_TOKEN_ID'; Hint = 'Token ID с help.altway.pro (источник переноса)' },
        @{ Name = 'BOOKSTACK_FROM_TOKEN_SECRET'; Hint = 'Token Secret с help.altway.pro' },
        @{ Name = 'BOOKSTACK_TO_TOKEN_ID'; Hint = 'Token ID с test.help.altway.pro (приёмник)' },
        @{ Name = 'BOOKSTACK_TO_TOKEN_SECRET'; Hint = 'Token Secret с test.help.altway.pro' }) }
}

Write-Host "Хранилище: $path"
Write-Host "Ввод скрытый. Пустой ответ — оставить как есть, Ctrl+C — выйти."
Write-Host ""

$saved = 0

foreach ($item in $wanted) {
    $secure = Read-Host -Prompt "$($item.Hint)  [$($item.Name)]" -AsSecureString

    if ($secure.Length -eq 0) {
        Write-Host "  пропущено" -ForegroundColor DarkGray
        continue
    }

    Save-BookStackToken -Root $scriptRoot -Name $item.Name -Value $secure
    Write-Host "  сохранено" -ForegroundColor Green
    $saved++
}

Write-Host ""

if ($saved -eq 0) {
    Write-Host "Ничего не изменилось."
    exit 0
}

Write-Host "Готово, значений сохранено: $saved" -ForegroundColor Green
Write-Host "Проверить, не запуская выкладку:  ./push-docs.ps1 -DryRun"
Write-Host "Что лежит в хранилище:           ./set-tokens.ps1 -Show"
