using AngleSharp;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SwimmingPoolScheduleNotifier.Models;
using SwimmingPoolScheduleNotifier.Services;
using Xunit;

namespace SwimmingPoolScheduleNotifier.Tests;

public class PoolScraperTests
{
    private const string SampleHtml = @"<ul class=""planning-page__events"">
  <li class=""planning-page__event"">
    <section aria-labelledby=""title-19-10-2026"" class=""planning-page-section planning-page-section--0"">
      <h2 id=""title-19-10-2026"" class=""planning-page-section__title"">
        <div class=""loading-skeleton"">Maandag 19 oktober 2026</div>
      </h2>
      <div class=""planning-page-section__content"">
        <article class=""planning-event"">
          <div class=""planning-event__content"">
            <p class=""planning-event__times"">
              <div class=""loading-skeleton""><time datetime=""2026-10-19T07:00:00+02:00"">07:00</time> - <time datetime=""2026-10-19T09:00:00+02:00"">09:00</time></div>
            </p>
            <div class=""planning-event__text-content""><h1 class=""planning-event__title"">Ochtendzwemmen</h1></div>
          </div>
        </article>
        <article class=""planning-event"">
          <div class=""planning-event__content"">
            <p class=""planning-event__times"">
              <div class=""loading-skeleton""><time datetime=""2026-10-19T11:30:00+02:00"">11:30</time> - <time datetime=""2026-10-19T13:00:00+02:00"">13:00</time></div>
            </p>
            <div class=""planning-event__text-content""><h1 class=""planning-event__title"">Middag Pauze Zwemmen</h1></div>
          </div>
        </article>
      </div>
    </section>
  </li>
  <li class=""planning-page__event"">
    <section aria-labelledby=""title-24-10-2026"" class=""planning-page-section planning-page-section--1"">
      <h2 id=""title-24-10-2026"" class=""planning-page-section__title"">
        <div class=""loading-skeleton"">Zaterdag 24 oktober 2026</div>
      </h2>
      <div class=""planning-page-section__content"">
        <article class=""planning-event"">
          <div class=""planning-event__content"">
            <p class=""planning-event__times"">
              <div class=""loading-skeleton""><time datetime=""2026-10-24T14:00:00+02:00"">14:00</time> - <time datetime=""2026-10-24T16:00:00+02:00"">16:00</time></div>
            </p>
            <div class=""planning-event__text-content""><h1 class=""planning-event__title"">Weekend Zwemmen</h1></div>
          </div>
        </article>
      </div>
    </section>
  </li>
</ul>";

    [Fact]
    public async Task ParseDaysFromDom_OnlyIncludesOchtendMiddagAndWeekend()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var service = new PoolScraperService(httpClientFactory.Object, NullLogger<PoolScraperService>.Instance);

        var browsingContext = BrowsingContext.New(Configuration.Default);
        var document = await browsingContext.OpenAsync(req => req.Content(SampleHtml));

        var days = service.ParseDaysFromDom(document);
        string result = PoolScraperService.FormatSingleLocationSchedule(new LocationScheduleModel
        {
            LocationName = "Zwembad Test",
            Days = days
        });

        Assert.NotNull(result);

        // Sections
        Assert.Contains("OCHTEND (07:00 - 09:00)", result);
        Assert.Contains("MIDDAG (15:00 - 18:00)", result);
        Assert.Contains("WEEKEND", result);

        // Grouped by pool
        Assert.Contains("Zwembad Test:", result);
        Assert.Contains("- Ma 19/10: 07:00 - 09:00", result);
        Assert.Contains("- Za 24/10: 14:00 - 16:00", result);

        // No emoticons
        Assert.DoesNotContain("🏊", result);
        Assert.DoesNotContain("🌅", result);
        Assert.DoesNotContain("☀️", result);
        Assert.DoesNotContain("🏖️", result);
        Assert.DoesNotContain("📅", result);

        // Should NOT include 11:30 - 13:00 (outside ochtend/middag/weekend)
        Assert.DoesNotContain("11:30 - 13:00", result);
        Assert.DoesNotContain("Middag Pauze Zwemmen", result);
    }
}
