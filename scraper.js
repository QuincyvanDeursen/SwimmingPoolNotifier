const { chromium } = require('playwright');

const NTFY_TOPIC_URL = process.env.NTFY_TOPIC_URL || 'https://ntfy.sh/zwem-rooster-breda';

const POOLS = [
    {
        name: 'Zwembad De Wisselaar',
        url: 'https://www.optisport.nl/activiteiten/banenzwemmen/zwembad-de-wisselaar-breda#locatie'
    },
    {
        name: 'Zwembad Sonsbeeck',
        url: 'https://www.optisport.nl/activiteiten/banenzwemmen/zwembad-sonsbeeck-breda#locatie'
    }
];

function extractTime(str) {
    if (!str) return '';
    const match = str.match(/T?(\d{2}:\d{2})/);
    return match ? match[1] : str;
}

function parseTime(timeStr) {
    if (!timeStr) return null;
    const match = timeStr.match(/T?(\d{1,2}):(\d{2})/);
    if (!match) return null;
    return parseInt(match[1], 10) * 60 + parseInt(match[2], 10);
}

function isTimeBetween(startStr, endStr, rangeStartMin, rangeEndMin) {
    const start = parseTime(startStr);
    const end = parseTime(endStr);
    if (start === null) return false;
    if (end !== null) {
        return start < rangeEndMin && end > rangeStartMin;
    }
    return start >= rangeStartMin && start <= rangeEndMin;
}

function getDayAbbrev(dayName, dateStr) {
    const lower = (dayName || '').toLowerCase();
    if (lower.includes('maandag')) return 'Ma';
    if (lower.includes('dinsdag')) return 'Di';
    if (lower.includes('woensdag')) return 'Wo';
    if (lower.includes('donderdag')) return 'Do';
    if (lower.includes('vrijdag')) return 'Vr';
    if (lower.includes('zaterdag')) return 'Za';
    if (lower.includes('zondag')) return 'Zo';
    return (dayName || '').split(' ')[0] || '';
}

function formatDayWithDate(dayName, dateStr) {
    const abbrev = getDayAbbrev(dayName, dateStr);
    if (dateStr) {
        // dateStr is usually DD-MM-YYYY or YYYY-MM-DD
        const parts = dateStr.includes('-') ? dateStr.split('-') : dateStr.split('/');
        if (parts.length === 3) {
            if (parts[0].length === 4) {
                // YYYY-MM-DD
                return `${abbrev} ${parts[2].padStart(2, '0')}/${parts[1].padStart(2, '0')}`;
            } else {
                // DD-MM-YYYY
                return `${abbrev} ${parts[0].padStart(2, '0')}/${parts[1].padStart(2, '0')}`;
            }
        }
    }

    // Try extract from dayName string (e.g. "Maandag 19 oktober 2026")
    const match = (dayName || '').match(/(\d{1,2})\s+([a-zA-Z]+)/);
    if (match) {
        const dayNum = match[1].padStart(2, '0');
        const monthMap = {
            jan: '01', feb: '02', mrt: '03', maa: '03', apr: '04', mei: '05', jun: '06',
            jul: '07', aug: '08', sep: '09', okt: '10', nov: '11', dec: '12'
        };
        const mKey = match[2].toLowerCase().substring(0, 3);
        const mNum = monthMap[mKey];
        if (mNum) {
            return `${abbrev} ${dayNum}/${mNum}`;
        }
    }

    return abbrev;
}

function isWeekend(dayName) {
    const lower = (dayName || '').toLowerCase();
    return lower.includes('zaterdag') || lower.includes('zondag');
}

function parseDateMs(dateStr) {
    if (!dateStr) return null;
    const parts = dateStr.includes('-') ? dateStr.split('-') : dateStr.split('/');
    if (parts.length === 3) {
        if (parts[0].length === 4) {
            return new Date(parseInt(parts[0], 10), parseInt(parts[1], 10) - 1, parseInt(parts[2], 10)).getTime();
        } else {
            return new Date(parseInt(parts[2], 10), parseInt(parts[1], 10) - 1, parseInt(parts[0], 10)).getTime();
        }
    }
    return null;
}

function formatSchedule(locations) {
    const morningStart = 7 * 60;
    const morningEnd = 9 * 60;
    const afternoonStart = 15 * 60;
    const afternoonEnd = 18 * 60;

    let minDateMs = null;
    for (const loc of locations) {
        for (const day of (loc.days || [])) {
            const ms = parseDateMs(day.date);
            if (ms !== null && (minDateMs === null || ms < minDateMs)) {
                minDateMs = ms;
            }
        }
    }
    const maxDateMs = minDateMs !== null ? minDateMs + 6 * 24 * 60 * 60 * 1000 : null;

    const sb = [];
    sb.push('Zwemrooster Breda');
    sb.push('=================');

    function appendSection(title, filterFn, weekendOnly, weekdayOnly) {
        sb.push('');
        sb.push(title);

        let anyFound = false;

        for (const loc of locations) {
            const matchingDays = [];

            for (const day of (loc.days || [])) {
                const dayMs = parseDateMs(day.date);
                if (maxDateMs !== null && dayMs !== null && dayMs > maxDateMs) continue;

                const isWk = isWeekend(day.day);
                if (weekendOnly && !isWk) continue;
                if (weekdayOnly && isWk) continue;

                const events = (day.events || []).filter(e => filterFn(e));
                if (events.length === 0) continue;

                const times = [...new Set(events.map(e => e.displayTime || `${e.start} - ${e.end}`))];
                const dayFormatted = formatDayWithDate(day.day, day.date);
                matchingDays.push({ dayFormatted, times });
            }

            if (matchingDays.length > 0) {
                anyFound = true;
                sb.push(`${loc.name}:`);
                for (const item of matchingDays) {
                    sb.push(`- ${item.dayFormatted}: ${item.times.join(', ')}`);
                }
            }
        }

        if (!anyFound) {
            sb.push('Geen');
        }
    }

    // 1. Ochtend
    appendSection(
        'OCHTEND (07:00 - 09:00)',
        e => isTimeBetween(e.start, e.end, morningStart, morningEnd),
        false,
        true
    );

    // 2. Middag
    appendSection(
        'MIDDAG (15:00 - 18:00)',
        e => isTimeBetween(e.start, e.end, afternoonStart, afternoonEnd),
        false,
        true
    );

    // 3. Weekend
    appendSection(
        'WEEKEND',
        () => true,
        true,
        false
    );

    return sb.join('\n').trim();
}

async function scrapePool(browser, pool) {
    console.log(`Starting scraper for ${pool.name}...`);
    const context = await browser.newContext({
        userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36',
        locale: 'nl-NL',
        viewport: { width: 1280, height: 800 }
    });

    const page = await context.newPage();

    let capturedSchedule = null;

    // Listen for the API response while the page loads
    page.on('response', async res => {
        if (res.url().includes('upcomingeventsschedule')) {
            try {
                const json = await res.json();
                if (json && Array.isArray(json.schedule)) {
                    capturedSchedule = json.schedule;
                    console.log(`Intercepted schedule API response for ${pool.name} with ${capturedSchedule.length} days.`);
                }
            } catch (err) {
                // Ignore parse errors
            }
        }
    });

    try {
        console.log(`Navigating to ${pool.url}...`);
        await page.goto(pool.url, { waitUntil: 'domcontentloaded', timeout: 45000 });

        // Wait up to 10 seconds for the schedule API call or DOM elements to populate
        for (let i = 0; i < 20; i++) {
            if (capturedSchedule && capturedSchedule.length > 0) break;
            await page.waitForTimeout(500);
        }

        if (capturedSchedule && capturedSchedule.length > 0) {
            // Limit to max 7 days
            const days = capturedSchedule.slice(0, 7).map(d => ({
                day: d.day,
                date: d.date,
                events: (d.events || []).map(e => {
                    const startTime = extractTime(e.start);
                    const endTime = extractTime(e.end);
                    return {
                        title: e.title,
                        start: startTime,
                        end: endTime,
                        displayTime: `${startTime} - ${endTime}`,
                        internalLocation: e.internalLocation
                    };
                })
            }));
            await context.close();
            return { name: pool.name, days };
        }

        // Fallback: parse from rendered DOM if API intercept didn't trigger
        console.log(`Checking DOM for rendered events for ${pool.name}...`);
        const domSections = await page.$$('section.planning-page-section');
        if (domSections.length > 0) {
            const days = [];
            for (const section of domSections.slice(0, 7)) {
                const titleEl = await section.$('.planning-page-section__title');
                const dayTitle = titleEl ? (await titleEl.textContent()).trim() : '';
                if (!dayTitle) continue;

                const eventArticles = await section.$$('article.planning-event');
                const events = [];
                for (const article of eventArticles) {
                    const timesEl = await article.$('.planning-event__times');
                    const timeText = timesEl ? (await timesEl.textContent()).trim() : '';
                    const match = timeText.match(/(\d{1,2}:\d{2})\s*-\s*(\d{1,2}:\d{2})/);
                    if (match) {
                        events.push({
                            title: 'Banenzwemmen',
                            start: match[1],
                            end: match[2],
                            displayTime: `${match[1]} - ${match[2]}`
                        });
                    }
                }

                days.push({ day: dayTitle, events });
            }

            await context.close();
            return { name: pool.name, days };
        }

        await context.close();
        throw new Error(`Could not find schedule data for ${pool.name}`);
    } catch (err) {
        await context.close();
        throw err;
    }
}

async function main() {
    console.log('Swimming Pool Schedule Job started (Playwright runner)...');
    const browser = await chromium.launch({
        headless: true,
        args: [
            '--no-sandbox',
            '--disable-setuid-sandbox',
            '--disable-blink-features=AutomationControlled'
        ]
    });

    const results = [];

    try {
        for (const pool of POOLS) {
            try {
                const data = await scrapePool(browser, pool);
                results.push(data);
            } catch (err) {
                console.error(`Error scraping ${pool.name}:`, err.message);
            }
        }
    } finally {
        await browser.close();
    }

    if (results.length === 0) {
        console.error('Failed to scrape any pool schedule.');
        await fetch(NTFY_TOPIC_URL, {
            method: 'POST',
            body: 'Let op: De scraper voor het zwemrooster is stuk. Controleer de website lay-out of broncode.',
            headers: { 'Title': 'Fout: Zwemrooster Scraper' }
        });
        process.exit(1);
    }

    const message = formatSchedule(results);
    console.log('\n--- FORMATTED SCHEDULE ---');
    console.log(message);
    console.log('--------------------------\n');

    console.log(`Sending notification to ${NTFY_TOPIC_URL}...`);
    const resp = await fetch(NTFY_TOPIC_URL, {
        method: 'POST',
        body: message,
        headers: {
            'Title': 'Zwemrooster Breda'
        }
    });

    if (resp.ok) {
        console.log('Notification successfully posted to ntfy.sh!');
    } else {
        console.error(`Failed to post to ntfy.sh: ${resp.status} ${resp.statusText}`);
        process.exit(1);
    }
}

main().catch(err => {
    console.error('Fatal error:', err);
    process.exit(1);
});
