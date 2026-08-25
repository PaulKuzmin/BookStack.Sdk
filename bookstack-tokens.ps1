<#
.SYNOPSIS
    Хранилище токенов BookStack для скриптов выкладки. Подключается точкой, сам ничего не делает.

.DESCRIPTION
    Токены нужны инструментам переменными окружения, но записывать их в постоянное окружение
    пользователя незачем: они осели бы в реестре и стали видны любому процессу этой учётной записи.
    Здесь они кладутся в файл рядом со скриптами, а в окружение попадают только на время запуска —
    в ТЕКУЩИЙ процесс, откуда их наследует запускаемый инструмент.

    Хранилищ два:

      tokens.secret.xml  предпочтительное. Значения зашифрованы средствами Windows (DPAPI) на
                         связку «эта учётная запись + эта машина». Файл, унесённый на другую
                         машину или открытый другим пользователем, не расшифруется.
                         Заводится скриптом ./set-tokens.ps1.

      tokens.env         запасное, простой текст вида ИМЯ=значение. Годится, когда файл нужен
                         переносимый, но тогда это секрет открытым текстом на диске.

    Оба имени внесены в .gitignore. В репозиторий такой файл попасть не должен.
#>

# Имена, которые понимают оба инструмента. Половинки токена живут отдельно намеренно: склеенные
# заранее, они однажды окажутся переставлены, а BookStack отвечает на это тем же 401, что и на
# чужой токен.
$script:BookStackTokenNames = @(
    'BOOKSTACK_TOKEN_ID',
    'BOOKSTACK_TOKEN_SECRET',
    'BOOKSTACK_FROM_TOKEN_ID',
    'BOOKSTACK_FROM_TOKEN_SECRET',
    'BOOKSTACK_TO_TOKEN_ID',
    'BOOKSTACK_TO_TOKEN_SECRET'
)

function Get-BookStackSecretPath {
    param([Parameter(Mandatory)] [string] $Root)
    Join-Path $Root 'tokens.secret.xml'
}

function Get-BookStackEnvPath {
    param([Parameter(Mandatory)] [string] $Root)
    Join-Path $Root 'tokens.env'
}

<#
.SYNOPSIS
    Сохраняет одно значение в зашифрованное хранилище, не трогая остальные.
#>
function Save-BookStackToken {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [System.Security.SecureString] $Value
    )

    $path = Get-BookStackSecretPath $Root
    $store = @{}

    if (Test-Path -LiteralPath $path) {
        # Хранилище читается целиком и переписывается целиком: править половину файла на месте
        # означало бы разбирать чужой формат ради экономии, которой тут нет.
        (Import-Clixml -LiteralPath $path).GetEnumerator() | ForEach-Object { $store[$_.Key] = $_.Value }
    }

    # ConvertFrom-SecureString без ключа шифрует через DPAPI: расшифровать сможет только эта
    # учётная запись на этой машине. Свой ключ тут был бы хуже — его пришлось бы где-то хранить.
    $store[$Name] = ConvertFrom-SecureString $Value
    $store | Export-Clixml -LiteralPath $path

    # Сужение прав на файл — добавка сверху, а не защита сама по себе: содержимое уже зашифровано
    # DPAPI и чужой учётной записи не поддастся. Поэтому неудача тут не повод шуметь: в песочнице
    # или на сетевом диске нужной привилегии может просто не быть.
    try {
        $acl = Get-Acl -LiteralPath $path
        $acl.SetAccessRuleProtection($true, $false)
        $acl.Access | ForEach-Object { [void]$acl.RemoveAccessRule($_) }

        foreach ($who in @([System.Security.Principal.WindowsIdentity]::GetCurrent().Name, 'SYSTEM')) {
            $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule(
                $who, 'FullControl', 'Allow')))
        }

        Set-Acl -LiteralPath $path -AclObject $acl
    }
    catch {
        Write-Verbose "Права на $path сузить не вышло: $($_.Exception.Message)"
    }
}

<#
.SYNOPSIS
    Кладёт токены из хранилища в окружение ТЕКУЩЕГО процесса.

.DESCRIPTION
    Ничего не пишет ни в реестр, ни в постоянное окружение: значения живут ровно до конца запуска.
    Уже заданные переменные окружения имеют приоритет — если кто-то задал токен руками в этом окне,
    файл его не перебивает.

.OUTPUTS
    Строка с описанием источника, годная для показа человеку (значений в ней нет).
#>
function Import-BookStackTokens {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [string] $TokenFile
    )

    # Названный явно файл разбирается по расширению, а не перебором: подсунутый не тот файл должен
    # дать понятную жалобу, а не тихо провалиться в следующую попытку.
    if ($TokenFile) {
        if (-not (Test-Path -LiteralPath $TokenFile)) {
            Write-Warning "Нет файла с токенами: $TokenFile"
            return $null
        }

        if ([IO.Path]::GetExtension($TokenFile) -eq '.xml') {
            return Format-BookStackTokenSource $TokenFile 'зашифровано' (Import-BookStackSecretFile $TokenFile)
        }

        return Format-BookStackTokenSource $TokenFile 'простой текст' (Import-BookStackEnvFile $TokenFile)
    }

    $secret = Get-BookStackSecretPath $Root
    if (Test-Path -LiteralPath $secret) {
        $result = Import-BookStackSecretFile $secret
        if ($result.Loaded -ge 0) { return Format-BookStackTokenSource $secret 'зашифровано' $result }
    }

    $plain = Get-BookStackEnvPath $Root
    if (Test-Path -LiteralPath $plain) {
        return Format-BookStackTokenSource $plain 'простой текст' (Import-BookStackEnvFile $plain)
    }

    return $null
}

<#
.SYNOPSIS
    Складывает строку о том, откуда взялись токены и сколько их.

.DESCRIPTION
    Разделять «взято из файла» и «уже было задано в этом окне» приходится потому, что скрипт,
    запущенный в одном окне дважды, во второй раз не берёт из файла НИЧЕГО: значения остались в
    окружении процесса с первого раза. Голое «значений 0» в такой раз читается как «токенов нет»,
    хотя всё на месте.
#>
function Format-BookStackTokenSource {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Kind,
        [Parameter(Mandatory)] $Result
    )

    if ($Result.Loaded -gt 0 -and $Result.Skipped -gt 0) {
        return "токены: $Path ($Kind): взято $($Result.Loaded), ещё $($Result.Skipped) уже заданы в этом окне"
    }

    if ($Result.Loaded -gt 0) {
        return "токены: $Path ($Kind): взято $($Result.Loaded)"
    }

    if ($Result.Skipped -gt 0) {
        return "токены: все $($Result.Skipped) уже заданы в этом окне, файл не понадобился"
    }

    return "токены: $Path ($Kind) — ни одного знакомого имени, проверьте содержимое"
}

function Import-BookStackSecretFile {
    param([Parameter(Mandatory)] [string] $Path)

    try {
        $store = Import-Clixml -LiteralPath $Path
    }
    catch {
        Write-Warning "Не читается $Path : $($_.Exception.Message)"
        return @{ Loaded = -1; Skipped = 0 }
    }

    $loaded = 0
    $skipped = 0

    foreach ($name in $script:BookStackTokenNames) {
        if (-not $store.ContainsKey($name)) { continue }

        # Заданное руками в этом окне не перебиваем, но и молчать о нём не надо: иначе повторный
        # запуск в том же окне выглядит как «файл пуст».
        if (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            $skipped++
            continue
        }

        try {
            $secure = ConvertTo-SecureString $store[$name]
        }
        catch {
            Write-Warning ("Значение $name не расшифровалось. Так бывает, когда файл сделан другой " +
                           "учётной записью или на другой машине: заведите его заново ./set-tokens.ps1.")
            continue
        }

        Set-ProcessEnvFromSecure -Name $name -Value $secure
        $loaded++
    }

    return @{ Loaded = $loaded; Skipped = $skipped }
}

function Import-BookStackEnvFile {
    param([Parameter(Mandatory)] [string] $Path)

    $loaded = 0
    $skipped = 0

    foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8) {
        $text = $line.Trim()
        if ($text.Length -eq 0 -or $text.StartsWith('#')) { continue }

        $split = $text.IndexOf('=')
        if ($split -le 0) { continue }

        $name = $text.Substring(0, $split).Trim()
        $value = $text.Substring($split + 1).Trim().Trim('"', "'")

        if ($script:BookStackTokenNames -notcontains $name) { continue }

        if (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
            $skipped++
            continue
        }

        # Область Process, а не User: значение исчезнет вместе с процессом и в реестр не попадёт.
        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        $loaded++
    }

    return @{ Loaded = $loaded; Skipped = $skipped }
}

<#
.SYNOPSIS
    Разворачивает SecureString в переменную окружения процесса, освобождая память сразу.
#>
function Set-ProcessEnvFromSecure {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [System.Security.SecureString] $Value
    )

    $pointer = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        [Environment]::SetEnvironmentVariable(
            $Name,
            [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer),
            'Process')
    }
    finally {
        # Иначе расшифрованная строка осталась бы лежать в неуправляемой памяти до конца процесса.
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}
