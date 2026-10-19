# 🤖 GitHub Actions Web Scraper & Push Notifier - Prompt Template

Gebruik dit template om snel een nieuwe geautomatiseerde scraper te laten genereren door een AI (zoals Antigravity, ChatGPT of Claude) die draait via **GitHub Actions** (100% gratis, in pure C# .NET 10, zonder cloud-kosten of servers).

Kopieer de onderstaande prompt, vul de velden tussen `[BLOKHAKEN]` in, en plak hem in de chat.

---

```markdown
Acteer als een expert .NET 10 C# ontwikkelaar gespecialiseerd in web scraping, browser automation (Microsoft.Playwright) en GitHub Actions CI/CD.

Ik wil een 100% gratis geautomatiseerde job bouwen op GitHub Actions (pure .NET 10 C# Console App, géén dubbele codebases of losse Node/Python scripts) die periodiek een website scraped en mij een pushnotificatie stuurt met de resultaten via ntfy.sh.

### 1. Doel & Brongegevens:
- Doel van de scraper: [BIJV: Wekelijks zwemrooster ophalen / Prijs van een product monitoren / Sportschema checken]
- Doel-URL('s): [PLAK HIER DE WEBSITE URL OF MEERDERE URLS]
- Doel ntfy.sh topic: https://ntfy.sh/[KIES_EEN_UNIEKE_TOPIC_NAAM]

### 2. Website Type & Data:
- Type site: [KIES: Dynamische SPA / Cloudflare-beveiligd / Statische HTML]
- Hier is een representatief stukje HTML of API data (gekopieerd via F12 Inspect Element / Network tab):

```html
[PLAK HIER HET STUKJE HTML OF JSON UIT DE NETWORK TAB]
```

### 3. Technische Eisen:
1. **Console App & Architectuur (Pure C# .NET 10):**
   - Één consistente C# .NET 10 Console Applicatie (geen losse JavaScript/Python scrapers ernaast).
   - Gebruik `Microsoft.Extensions.DependencyInjection` en `Microsoft.Extensions.Logging`.
   - Lees configuraties (URL's en ntfy topic) uit environment variables met nette standaarden.

2. **Scraping Logica & Anti-Bot Bypassing:**
   - Gebruik **`Microsoft.Playwright` (C#)** om websites te laden (met name bij Cloudflare-beveiliging of dynamische Vue/React/Angular SPA's).
   - Start Chromium headless met `--no-sandbox` en `--disable-blink-features=AutomationControlled`.
   - Luister via `page.Response` om onderliggende JSON API calls direct te onderscheppen (veel betrouwbaarder en sneller dan HTML scrapen).
   - Voorzie een fallback via DOM parsing (`AngleSharp` of Playwright selectors) als er geen API response is.

3. **Filtering & Layout Eisen:**
   - [SPECIFIEKE FILTERWENSEN: BIJV. "Alleen banenzwemmen in de ochtend (07:00-09:00), middag (15:00-18:00) en het hele weekend"].
   - Groepeer logisch (bijv. per dagdeel, per locatie of per categorie).
   - Weergave datum: `dd/MM` (bijv. `- Ma 20/10: 07:00 - 09:00`).
   - Geen onnodige emoticons (tenzij expliciet gevraagd), houd de notificatietekst compact en to-the-point.

4. **Notificatie (ntfy.sh):**
   - Stuur een HTTP POST request naar: `https://ntfy.sh/[TOPIC_NAAM]`.
   - Header `Title: [TITEL VAN JE BERICHT]`.
   - Body: compacte, overzichtelijke UTF-8 platte tekst.

5. **Error Handling & Failsafe:**
   - Log duidelijke consoleberichten bij elke stap.
   - Als het scrapen mislukt, stuur direct een alertnotificatie naar ntfy:
     `"Let op: De scraper voor [DOEL] is mislukt. Controleer de website lay-out of broncode."`
   - Exit met code 1 bij falen zodat GitHub Actions de run direct rood markeert.

6. **GitHub Actions Workflow (`.github/workflows/scrape-schedule.yml`):**
   - Runner: `ubuntu-latest`.
   - Setup van `.NET 10` via `actions/setup-dotnet@v4`.
   - **Caching toevoegen voor snelheid en minimale belasting:**
     - NuGet pakketten cache (`~/.nuget/packages`).
     - Playwright Chromium cache (`~/.cache/ms-playwright`).
   - Browser installatie via het Playwright powershell script:
     `pwsh <project_pad>/bin/Release/net10.0/playwright.ps1 install --with-deps chromium`.
   - Uitvoeren via `dotnet run --no-build --configuration Release`.
   - Schedule trigger via cron (bijv. wekelijks op zondagavond) + `workflow_dispatch` voor handmatig testen met één klik.
```

---

## 💡 Hoe gebruik je dit template?

### Stap 1: Inspecteer de pagina
1. Open de gewenste website in Chrome of Edge.
2. Druk op **F12** en ga naar het tabblad **Network** (Filter op `Fetch/XHR`).
3. Herlaad de pagina. Vaak zie je direct een schone JSON response met alle data (zoals bij Optisport `/upcomingeventsschedule`).
4. Zo niet, gebruik het tabblad **Elements** en kopieer het relevante HTML-gedeelte.

### Stap 2: Kies een ntfy topic
1. Bedenk een unieke topic-naam (bijv. `mijn-zwemschema-2026`).
2. Installeer de gratis **ntfy app** op Android of iOS en abonneer je op dit topic (of open [ntfy.sh/jouw-topic](https://ntfy.sh)).

### Stap 3: Plak in AI & Push naar GitHub
Plak het ingevulde template in Antigravity of ChatGPT. Laat de C# projectbestanden en de GitHub workflow genereren, push naar je GitHub repo en je scraper draait direct gratis en automatisch in de cloud!
