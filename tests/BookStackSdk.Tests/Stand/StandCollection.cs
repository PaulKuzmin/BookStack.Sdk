using Xunit;

namespace BookStackSdk.Tests.Stand;

/// <summary>
/// Коллекция стендовых проверок BookStack: все классы с пометкой
/// <c>[Collection(nameof(StandCollection))]</c> идут ПОСЛЕДОВАТЕЛЬНО и делят одну
/// <see cref="StandFixture"/>.
/// </summary>
/// <remarks>
/// Зачем последовательно. Стенд один на всех, и параллельные классы дрались бы за него: поиск
/// BookStack ищет по всему содержимому установки и находил бы пробы соседнего теста, счётчик
/// <c>total</c> в списках менялся бы под ногами, уборка одного класса шла бы посреди проверок
/// другого, а журнал уборки писался бы из двух потоков. Проверки без сети (на подставном
/// обработчике) это не касается, они остаются параллельными.
/// <para>
/// Заодно фикстура поднимается один раз на всю коллекцию: живость стенда, приём токена и
/// опознание установки (<see cref="ProductionGuard"/>) делаются однажды, а не перед каждым
/// классом.
/// </para>
/// </remarks>
[CollectionDefinition(nameof(StandCollection))]
public sealed class StandCollection : ICollectionFixture<StandFixture>;
