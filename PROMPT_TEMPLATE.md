# 🤖 GitHub Actions Web Scraper & Push Notifier - Prompt Template

Gebruik dit template om snel een nieuwe geautomatiseerde scraper te laten genereren door een AI (zoals Antigravity, ChatGPT of Claude) die draait via **GitHub Actions** (100% gratis, zonder cloud-accounts of creditcards).

Kopieer de onderstaande prompt, vul de velden tussen `[BLOKHAKEN]` in, en plak hem in de chat.

---

```markdown
Acteer als een expert .NET 10 C# ontwikkelaar gespecialiseerd in web scraping en GitHub Actions automation.

Ik wil een 100% gratis geautomatiseerde job bouwen op GitHub Actions (.NET 10 Console App) die periodiek een website scraped en mij een pushnotificatie stuurt met de resultaten via ntfy.sh.

### 1. Doel & Brongegevens:
- Doel van de scraper: [BIJV: Wekelijks zwemrooster ophalen / Prijs van een product monitoren / Sportschema checken]
- Doel-URL: [PLAK HIER DE WEBSITE URL]
- Doel ntfy.sh topic: https://ntfy.sh/[KIES_EEN_UNIEKE_TOPIC_NAAM]

### 2. Voorbeeld HTML van de doelsite:
Hier is een representatief stukje HTML van de data die ik wil scrapen (gekopieerd via F12 Inspect Element):

```html
[PLAK HIER HET STUKJE HTML MET DE SELECTORS EN DATA DIE JE WILT HEBBEN]
```

### 3. Technische Eisen:
1. **Console App & Architectuur:**
   - Bouw een schone, snelle .NET 10 Console Applicatie (geen logge cloud/Azure dependencies).
   - Gebruik `IHttpClientFactory` met dependency injection.
   - Lees configuraties (URL's en ntfy topic) uit environment variables met nette fallbacks.

2. **Scraping Logica (AngleSharp):**
   - Gebruik `AngleSharp` om de HTML te parsen met CSS-selectors.
   - Verwijder storende elementen zoals `<script>` en `<style>` tags vóórdat tekst wordt geëxtraheerd.
   - Schoon de tekst op (trim overtollige spaties, normaliseer witregels en regeleindes).
   - [OPTIONELE FILTER- OF SORTEERWENSEN: BIJV: "Plaats tijden tussen 15:00 en 18:00 bovenaan als hoofdfocus", of "Alleen als prijs lager is dan €50"].

3. **Anti-Bot & Resilient Fetching:**
   - Configureer `IHttpClientFactory` met realistische browser-headers (`User-Agent`, `Accept`, `Accept-Language`) zodat Cloudflare of basis bot-bescherming de scraper niet met een 403 Forbidden blokkeert.
   - Als de data dynamisch client-side wordt ingeladen (zoals via Vue/React), analyseer dan of er een onderliggende API/JSON call is en voorzie een robuuste fallback.

4. **Notificatie (ntfy.sh):**
   - Stuur een HTTP POST request naar: `https://ntfy.sh/[KIES_EEN_UNIEKE_TOPIC_NAAM]`.
   - Headers:
     - `Title: [TITEL VAN JE BERICHT]`
     - `Tags: [BIJV: swimmer,calendar of shopping_cart,bell]` (voegt emoji's toe in ntfy).
   - Body: opgeschoonde, geformatteerde tekst (UTF-8 plain text).

5. **Error Handling:**
   - Vang fouten op met try/catch en log ze naar de console.
   - Als het scrapen mislukt, stuur dan direct een noodnotificatie naar hetzelfde ntfy topic met:
     `"⚠️ Let op: De scraper voor [NAAM_DOEL] is mislukt. Controleer de website lay-out of broncode."` met tags `warning,rotating_light`.
   - Exit met code 1 bij falen zodat GitHub Actions workflow logs het direct rood markeren.

6. **GitHub Actions Workflow (`.github/workflows/scrape.yml`):**
   - Draai op `ubuntu-latest` met setup van `.NET 10`.
   - Schedule trigger via cron: "[BIJV: '0 19 * * 0' (wekelijks zondag)]".
   - `workflow_dispatch` trigger toevoegen zodat de scraper ook handmatig met één klik op GitHub kan worden gestart.
```

---

## 💡 Hoe gebruik je dit template?

### Stap 1: Vind de HTML van de pagina
1. Open de gewenste website in Chrome of Edge.
2. Druk op **F12** (of klik met de rechtermuisknop op de tekst die je wilt scrapen en kies **Inspecteren**).
3. Zoek het omringende element (bijv. `<div class="rooster">...</div>`).
4. Klik met de rechtermuisknop op die regel ➔ **Copy** ➔ **Copy element**.

### Stap 2: Kies een ntfy topic
1. Ga naar [ntfy.sh](https://ntfy.sh) of open de ntfy app op je telefoon.
2. Bedenk een unieke topic naam (bijv. `mijn-tennis-rooster-2026`).

### Stap 3: Plak in AI & Push naar GitHub
Plak het ingevulde template in Antigravity of ChatGPT. Push de bestanden naar GitHub en je geautomatiseerde scraper draait direct gratis in de cloud!
