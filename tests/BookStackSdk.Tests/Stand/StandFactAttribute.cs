using Xunit;

namespace BookStackSdk.Tests.Stand;

/// <summary>
/// <see cref="FactAttribute"/> для проверки против ЖИВОГО стенда BookStack.
/// </summary>
/// <remarks>
/// Пропуск ставится при ОБНАРУЖЕНИИ теста, а не бросается посреди тела: тогда причина попадает в
/// отчёт прогона отдельной строкой рядом с именем теста, а не прячется в исключении. Тот же приём,
/// что у <c>BulkFactAttribute</c> в AltWayService. Метод <c>Assert.Skip</c> в xUnit 2.9.3
/// недоступен, поэтому наследник атрибута это единственный способ пропустить тест с причиной.
/// <para>
/// ⚠️ ГЛАВНОЕ ЗДЕСЬ: в СТРОГОМ режиме (<see cref="StrictStand"/>) <c>Skip</c> не ставится вообще
/// ни при каких условиях. Тест уходит в прогон, добирается до <see cref="StandFixture"/> и падает
/// с адресом и причиной. Так пропуск превращается в отказ: на сборочном агенте и при приёмке
/// «зелено, потому что ничего не выполнялось» невозможно.
/// </para>
/// <para>
/// Атрибут отвечает только за «есть ли настроенный стенд». За то, что по адресу именно стенд, а
/// не боевой портал, отвечает <see cref="ProductionGuard"/>, и его отказ пропуском не бывает
/// никогда.
/// </para>
/// <para>
/// Замок на само это поведение стоит в <see cref="StrictModeGuardTests"/>, и он обычный
/// <see cref="FactAttribute"/>, то есть сам пропаститься не может.
/// </para>
/// </remarks>
public sealed class StandFactAttribute : FactAttribute
{
    public StandFactAttribute()
    {
        if (StrictStand.IsEnabled)
            return;

        if (!StandGate.IsConfigured)
            Skip = "Стенд BookStack: " + StandGate.Unavailable;
    }
}
