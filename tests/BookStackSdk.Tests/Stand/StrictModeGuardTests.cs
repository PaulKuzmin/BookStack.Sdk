using System;
using Xunit;

namespace BookStackSdk.Tests.Stand;

/// <summary>
/// Замок на сам приём гейтования: в строгом режиме стендовые проверки обязаны ИДТИ, а не
/// пропускаться.
/// </summary>
/// <remarks>
/// ⚠️ Здесь обычный <see cref="FactAttribute"/>, а не <see cref="StandFactAttribute"/>, и это
/// главное свойство этого класса: он не может пропаститься ни при какой настройке машины.
/// Пропускающий сам себя сторож бесполезен ровно тогда, когда он нужен.
/// <para>
/// Класс НЕ входит в <see cref="StandCollection"/> намеренно: ему не нужны ни докер, ни сеть, ни
/// фикстура. Он обязан отрабатывать на голой машине и говорить ровно одно: если прогон объявлен
/// строгим, стенд обязан быть настроен.
/// </para>
/// <para>
/// Зачем это вообще. В проекте уже дважды случалось, что стендовые проверки были зелёными
/// впустую: тесты не выполнялись, а отчёт выглядел успешным. Гейт по переменным окружения делает
/// такой исход ЗАКОННЫМ на машине разработчика, и цена этому одна: должно существовать место, где
/// закон отменяется. Это оно.
/// </para>
/// </remarks>
public class StrictModeGuardTests
{
    [Fact(DisplayName = "Строгий режим требует настроенного стенда, иначе прогон краснеет")]
    public void Strict_mode_demands_a_configured_stand()
    {
        if (!StrictStand.IsEnabled)
            return; // Обычный прогон: пропуск стендовых проверок разрешён, требовать нечего.

        Assert.True(
            StandGate.IsConfigured,
            $"Прогон объявлен строгим ({StrictStand.Variable}), но стенд BookStack не настроен: " +
            $"{StandGate.Unavailable} {StrictStand.Demand}");
    }

    [Fact(DisplayName = "Пропуск стендовых проверок ставится только вне строгого режима")]
    public void Stand_attribute_skips_only_outside_strict_mode()
    {
        // Атрибут читает то же состояние, что и прогон, поэтому проверяем не «пропущено или нет»,
        // а ПРАВИЛО целиком: пропуск допустим ровно в одном случае, когда режим нестрогий и стенда
        // нет. Правило проверяемо на любой машине, при любых переменных.
        var skip = new StandFactAttribute().Skip;
        var mustRun = StrictStand.IsEnabled || StandGate.IsConfigured;

        if (mustRun)
        {
            Assert.True(
                skip is null,
                "Стендовая проверка обязана идти в прогон (строгий режим либо настроенный стенд), " +
                $"а атрибут поставил пропуск: {skip}");
        }
        else
        {
            Assert.False(
                string.IsNullOrWhiteSpace(skip),
                "Стенда нет и режим нестрогий: тест обязан пропускаться с НАЗВАННОЙ причиной, " +
                "иначе прогон покраснеет от ненастроенной машины, а не от сломанного кода");
            Assert.Contains(StandGate.UrlVariable, skip!, StringComparison.Ordinal);
        }
    }

    [Fact(DisplayName = "Строгий режим читается прямо из окружения процесса")]
    public void Strict_switch_comes_from_the_environment_only()
    {
        // ⚠️ Замок на решение, а не на его последствия. Ровно на подмешивании конфигурации
        // приложения (appsettings, пользовательские секреты, in-memory источник) сломался
        // DapperPlusLicenseBootTests: исход теста стал зависеть от того, какие источники настроек
        // сегодня подцепились на машине. Если кто-то заведёт сюда IConfiguration, это равенство
        // перестанет держаться и покажет подмену.
        Assert.Equal(
            StrictStand.IsTruthy(Environment.GetEnvironmentVariable(StrictStand.Variable)),
            StrictStand.IsEnabled);
    }

    [Fact(DisplayName = "Непонятное значение включает строгий режим, а не выключает")]
    public void Unrecognized_value_turns_strictness_on()
    {
        Assert.False(StrictStand.IsTruthy(null));
        Assert.False(StrictStand.IsTruthy(string.Empty));
        Assert.False(StrictStand.IsTruthy("   "));
        Assert.False(StrictStand.IsTruthy("0"));
        Assert.False(StrictStand.IsTruthy("false"));
        Assert.False(StrictStand.IsTruthy("FALSE"));
        Assert.False(StrictStand.IsTruthy("off"));
        Assert.False(StrictStand.IsTruthy("no"));

        Assert.True(StrictStand.IsTruthy("1"));
        Assert.True(StrictStand.IsTruthy("true"));
        Assert.True(StrictStand.IsTruthy("yes"));

        // Опечатка обязана включать строгость, а не тихо выключать её: человек, который написал
        // сюда «да», ждёт строгого прогона, и молчаливый пропуск всех проверок обманул бы его
        // именно в тот момент, ради которого режим и заведён.
        Assert.True(StrictStand.IsTruthy("да"));
        Assert.True(StrictStand.IsTruthy("please"));
    }
}
