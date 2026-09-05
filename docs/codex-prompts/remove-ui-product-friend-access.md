# ТЗ: удалить product friend access из `CSharpFar.Ui`

## Цель

Удалить зависимость тестов продукта `CSharpFar.Tests` от `internal` API библиотеки `CSharpFar.Ui`.

После доработки:

- `CSharpFar.Ui` не предоставляет `InternalsVisibleTo` для `CSharpFar.Tests`;
- `CSharpFar.Tests` проверяет UI-интеграцию только через поддерживаемый `public` contract `CSharpFar.Ui`;
- white-box тесты внутренних деталей самой UI-библиотеки остаются в `CSharpFar.Ui.Tests` и могут использовать `InternalsVisibleTo`;
- если продуктовый integration test действительно требует возможности, которой нет в public API, необходимо определить минимальную семантическую reusable abstraction и только при наличии реального consumer-сценария сделать её public.

Это архитектурная очистка границы reusable UI library перед отдельной упаковкой/публикацией библиотеки.

## Текущее состояние

Сейчас `src/CSharpFar.Ui/CSharpFar.Ui.csproj` предоставляет friend access двум тестовым assembly:

- `CSharpFar.Ui.Tests` — корректный white-box consumer библиотеки;
- `CSharpFar.Tests` — product-level тесты приложения.

`tests/CSharpFar.Architecture.Tests/ReusableTestDependencyTests.cs` также явно разрешает `CSharpFar.Tests` как friend `CSharpFar.Ui` с объяснением про modal lifecycle и routed input.

Именно второе исключение требуется удалить.

## Основной принцип

`CSharpFar.Tests` должен вести себя как внешний consumer `CSharpFar.Ui`.

Тест продукта не должен получать более широкий доступ к UI-библиотеке, чем получит обычное приложение, использующее `CSharpFar.Ui` как отдельную зависимость.

Нельзя сохранять скрытую coupling между продуктом и implementation details библиотеки только ради удобства тестов.

## Требуемая работа

### 1. Найти фактические зависимости `CSharpFar.Tests` от `internal` API UI

Использовать удаление `InternalsVisibleTo("CSharpFar.Tests")` как compile-time probe и получить полный список мест в `CSharpFar.Tests`, которые перестают компилироваться.

Для каждого такого места определить, зачем тест обращается к internal API.

Не ограничиваться текстовым поиском по заранее известным типам: источником истины должны быть compilation errors после удаления friend access.

### 2. Классифицировать каждый найденный случай

Для каждого обращения выбрать один из вариантов ниже.

#### A. Тест проверяет внутреннюю механику reusable UI

Например, внутренний layout/frame/snapshot/router/controller lifecycle, который не является контрактом приложения.

Действие:

- если такой white-box тест действительно нужен — перенести соответствующую проверку в `CSharpFar.Ui.Tests`;
- если поведение уже достаточно защищено UI-тестами или более высоким тестом — удалить дублирующую product-level проверку;
- product test должен проверять только наблюдаемое поведение через public API.

#### B. Тест проверяет product behavior, но использует UI internal как shortcut

Действие:

- переписать тест через public application/UI contract;
- поднимать тест на наиболее высокий стабильный уровень, который доказывает нужное поведение;
- не открывать UI implementation detail только для сохранения существующей структуры теста.

#### C. Для реального внешнего consumer-сценария действительно отсутствует необходимая capability

Только в этом случае допускается изменение public API `CSharpFar.Ui`.

Требования к такому изменению:

- abstraction должна быть reusable и не содержать CSharpFar/file-manager semantics;
- API должен описывать capability/semantic operation, а не внутреннюю структуру реализации;
- public поверхность должна быть минимальной;
- нельзя делать public существующий internal type целиком только потому, что его использует тест;
- новое API должно быть полезно реальному приложению-потребителю, а не только тестовому проекту;
- новое API должно получить прямые тесты в `CSharpFar.Ui.Tests` и, если это boundary decision, соответствующую архитектурную защиту.

Если такой legit consumer-сценарий не удаётся сформулировать без ссылки на конкретный тест — public API расширять нельзя.

### 3. Удалить product friend access

Из `src/CSharpFar.Ui/CSharpFar.Ui.csproj` удалить:

```xml
<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
  <_Parameter1>CSharpFar.Tests</_Parameter1>
</AssemblyAttribute>
```

Оставить friend access для:

```text
CSharpFar.Ui.Tests
```

если он по-прежнему нужен для white-box тестов библиотеки.

### 4. Обновить архитектурный guardrail

В `ReusableTestDependencyTests.ReusableFriends_AreExplicitAndLimited` ожидаемый friend list для `CSharpFar.Ui` должен содержать только legitimate library test assembly.

Целевое состояние:

```csharp
AssertFriends(typeof(FormControls).Assembly, "CSharpFar.Ui.Tests");
```

Удалить комментарий/исключение, объясняющее product friend access через modal scopes/routed input.

Архитектурный тест должен не позволять незаметно вернуть `CSharpFar.Tests` в friend list в будущем.

### 5. Сохранить смысл integration tests

Не требуется механически сохранять текущую структуру каждого теста.

Требуется сохранить доказательство значимого поведения.

Если существующий тест одновременно проверяет application orchestration и внутренний UI lifecycle, его допустимо разделить:

- reusable UI lifecycle → `CSharpFar.Ui.Tests`;
- application integration → `CSharpFar.Tests` через public API.

Не дублировать один и тот же сценарий на нескольких уровнях без необходимости.

## Запрещённые решения

Нельзя:

- оставлять `InternalsVisibleTo("CSharpFar.Tests")`;
- заменить его другим product-specific friend assembly;
- использовать reflection, `dynamic`, private accessor, unsafe tricks или подобный обход visibility;
- добавлять public members с назначением только для тестов (`ForTests`, `TestOnly`, debug/test hooks без production semantics);
- массово переводить internal UI types в public;
- переносить product-specific semantics в `CSharpFar.Ui`;
- копировать внутреннюю UI-логику в product tests;
- ослаблять существующие public-boundary/architecture tests ради прохождения изменений.

## Scope

В scope:

- `CSharpFar.Ui` → `CSharpFar.Tests` friend relationship;
- product tests, которые перестанут компилироваться после его удаления;
- минимальные необходимые корректировки `CSharpFar.Ui.Tests`;
- архитектурные тесты friend boundary;
- минимальное public API изменение, только если оно обосновано legitimate reusable consumer scenario.

Не в scope:

- удаление `InternalsVisibleTo` из остальных product assemblies;
- полный пересмотр тестовой архитектуры всего решения;
- packaging/NuGet metadata;
- перенос `CSharpFar.Ui` в отдельный repository;
- общий redesign modal/render/input architecture;
- расширение public API ради будущих гипотетических потребителей.

## Предпочтительный порядок выполнения

1. Запустить текущие релевантные тесты и зафиксировать baseline.
2. Удалить `CSharpFar.Tests` из `InternalsVisibleTo` UI-проекта.
3. Собрать `CSharpFar.Tests` и получить полный compile-time inventory нарушений.
4. Для каждого нарушения применить классификацию A/B/C выше.
5. Перенести только legitimate white-box UI tests в `CSharpFar.Ui.Tests`.
6. Переписать product integration tests через public contract.
7. Если обнаружен реальный gap public API — добавить минимальную reusable abstraction и защитить её тестами.
8. Обновить `ReusableTestDependencyTests`.
9. Запустить reusable UI tests, architecture tests, product tests и полный solution test suite.
10. Проверить diff на отсутствие случайного public API expansion и unrelated refactoring.

## Acceptance criteria

Задача считается выполненной, если одновременно выполнены все условия:

1. В `CSharpFar.Ui` отсутствует `InternalsVisibleTo("CSharpFar.Tests")`.
2. Единственный test friend `CSharpFar.Ui` — `CSharpFar.Ui.Tests`, если white-box access всё ещё необходим.
3. `ReusableFriends_AreExplicitAndLimited` фиксирует новое ограничение и падает при возврате product friend access.
4. `CSharpFar.Tests` компилируется без доступа к internals `CSharpFar.Ui`.
5. Product integration tests используют только public UI contract.
6. Проверки implementation details UI находятся только в `CSharpFar.Ui.Tests` либо удалены как избыточные.
7. Ни reflection, ни test-only hooks, ни visibility bypass не используются.
8. Ни один UI type/member не стал public исключительно ради тестов.
9. Любое новое public API имеет сформулированный reusable consumer scenario и прямое тестовое покрытие в `CSharpFar.Ui.Tests`.
10. Existing `UiPublicBoundaryTests` и прочие architecture tests проходят без ослабления правил.
11. Проходят как минимум:

```bash
dotnet test tests/CSharpFar.Ui.Tests/CSharpFar.Ui.Tests.csproj
dotnet test tests/CSharpFar.Architecture.Tests/CSharpFar.Architecture.Tests.csproj
dotnet test tests/CSharpFar.Tests/CSharpFar.Tests.csproj
```

12. После этого проходит полный набор тестов solution/repository, используемый проектом перед merge.
13. Итоговый diff ограничен этой boundary-cleanup задачей и необходимыми test adaptations.

## Definition of done

`CSharpFar.Ui` можно рассматривать как независимую reusable library относительно product tests: приложение не получает привилегированного доступа к её implementation details, а тесты продукта доказывают интеграцию тем же public contract, который доступен внешнему consumer.