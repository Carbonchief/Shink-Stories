import fs from "node:fs/promises";
import path from "node:path";
import { SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const repoRoot = "/Users/luanvanderwalt/Documents/Websites/Shink-Stories";
const configPath = path.join(repoRoot, "Shink/appsettings.Development.json");
const outputDir = path.join(repoRoot, "outputs/free-only-subscribers-20260622");
const outputPath = path.join(outputDir, "free-only-subscribers.xlsx");

const config = JSON.parse(await fs.readFile(configPath, "utf8"));
const supabaseUrl = config.Supabase?.Url;
const apiKey = config.Supabase?.SecretKey;

if (!supabaseUrl || !apiKey) {
  throw new Error("Supabase URL or SecretKey is missing from appsettings.Development.json.");
}

const headers = {
  apikey: apiKey,
  Authorization: `Bearer ${apiKey}`,
  Accept: "application/json",
};

async function fetchPaged(table, select = "*") {
  const rows = [];
  const pageSize = 1000;

  for (let offset = 0; ; offset += pageSize) {
    const url = new URL(`/rest/v1/${table}`, supabaseUrl);
    url.searchParams.set("select", select);
    url.searchParams.set("limit", String(pageSize));
    url.searchParams.set("offset", String(offset));

    const response = await fetch(url, { headers });
    if (!response.ok) {
      const body = await response.text();
      throw new Error(`Supabase ${table} fetch failed: ${response.status} ${body}`);
    }

    const batch = await response.json();
    rows.push(...batch);
    if (batch.length < pageSize) {
      break;
    }
  }

  return rows;
}

function asDate(value) {
  if (!value) {
    return null;
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

function isGratisTier(row) {
  return String(row.tier_code ?? "").trim().toLowerCase() === "gratis";
}

function isCurrentlyActiveSubscription(row, now) {
  if (String(row.status ?? "").trim().toLowerCase() !== "active") {
    return false;
  }

  const cancelledAt = asDate(row.cancelled_at);
  if (cancelledAt && cancelledAt <= now) {
    return false;
  }

  const nextRenewalAt = asDate(row.next_renewal_at);
  if (nextRenewalAt && nextRenewalAt < now) {
    return false;
  }

  const sourceSystem = String(row.source_system ?? "").trim().toLowerCase();
  const hasOpenEndedImportedPaidAccess =
    !isGratisTier(row) &&
    (sourceSystem === "wordpress_pmpro" || sourceSystem === "discount_code") &&
    String(row.status ?? "").trim().toLowerCase() === "active" &&
    !cancelledAt &&
    !nextRenewalAt;

  if (hasOpenEndedImportedPaidAccess) {
    return true;
  }

  if (!isGratisTier(row) && !nextRenewalAt) {
    return false;
  }

  return true;
}

function hasPaidSubscriptionRow(row) {
  const tierCode = String(row.tier_code ?? "").trim().toLowerCase();
  return tierCode !== "" && tierCode !== "gratis";
}

function clean(value) {
  if (value === null || value === undefined) {
    return "";
  }

  return String(value).trim();
}

function joined(values) {
  return [...new Set(values.map(clean).filter(Boolean))].join(", ");
}

function earliestIso(rows, field) {
  const dates = rows.map((row) => asDate(row[field])).filter(Boolean);
  if (dates.length === 0) {
    return "";
  }

  dates.sort((a, b) => a - b);
  return dates[0];
}

function latestIso(rows, field) {
  const dates = rows.map((row) => asDate(row[field])).filter(Boolean);
  if (dates.length === 0) {
    return "";
  }

  dates.sort((a, b) => b - a);
  return dates[0];
}

function dateOnly(value) {
  const date = asDate(value);
  return date ? date.toISOString().slice(0, 10) : "";
}

function dateTime(value) {
  const date = asDate(value);
  return date ? date.toISOString().replace("T", " ").slice(0, 16) : "";
}

const now = new Date();
const [subscribers, subscriptions] = await Promise.all([
  fetchPaged("subscribers"),
  fetchPaged("subscriptions"),
]);

const subscriptionsBySubscriber = new Map();
for (const subscription of subscriptions) {
  const subscriberId = subscription.subscriber_id;
  if (!subscriberId) {
    continue;
  }

  const bucket = subscriptionsBySubscriber.get(subscriberId) ?? [];
  bucket.push(subscription);
  subscriptionsBySubscriber.set(subscriberId, bucket);
}

const freeOnly = subscribers
  .filter((subscriber) => !subscriber.disabled_at)
  .map((subscriber) => {
    const rows = subscriptionsBySubscriber.get(subscriber.subscriber_id) ?? [];
    const activeFreeRows = rows.filter((row) => isGratisTier(row) && isCurrentlyActiveSubscription(row, now));
    const paidRows = rows.filter(hasPaidSubscriptionRow);

    return { subscriber, rows, activeFreeRows, paidRows };
  })
  .filter((item) => item.activeFreeRows.length > 0 && item.paidRows.length === 0)
  .sort((a, b) => clean(a.subscriber.email).localeCompare(clean(b.subscriber.email), "en"));

const dataHeaders = [
  "Email",
  "First Name",
  "Last Name",
  "Display Name",
  "Mobile Number",
  "Subscriber ID",
  "Joined At",
  "Active Free Subscription Count",
  "Free Tier Codes",
  "Free Providers",
  "Free Source Systems",
  "First Free Subscribed At",
  "Latest Free Updated At",
];

const dataRows = freeOnly.map(({ subscriber, activeFreeRows }) => [
  clean(subscriber.email).toLowerCase(),
  clean(subscriber.first_name),
  clean(subscriber.last_name),
  clean(subscriber.display_name),
  clean(subscriber.mobile_number),
  clean(subscriber.subscriber_id),
  dateOnly(subscriber.created_at),
  activeFreeRows.length,
  joined(activeFreeRows.map((row) => row.tier_code)),
  joined(activeFreeRows.map((row) => row.provider)),
  joined(activeFreeRows.map((row) => row.source_system)),
  dateTime(earliestIso(activeFreeRows, "subscribed_at")),
  dateTime(latestIso(activeFreeRows, "updated_at")),
]);

const workbook = Workbook.create();
const summary = workbook.worksheets.add("Summary");
const sheet = workbook.worksheets.add("Free Only Subscribers");
summary.showGridLines = false;
sheet.showGridLines = false;

summary.getRange("A1:E1").merge();
summary.getRange("A1").values = [["Free Only Subscribers Export"]];
summary.getRange("A1").format.font = { bold: true, size: 18, color: "#FFFFFF" };
summary.getRange("A1").format.fill = { color: "#222222" };
summary.getRange("A1").format.rowHeightPx = 34;

const summaryRows = [
  ["Generated At", now],
  ["Included Subscribers", freeOnly.length],
  ["Source Subscribers Fetched", subscribers.length],
  ["Source Subscriptions Fetched", subscriptions.length],
  ["Filter", "Active gratis subscription, subscriber not disabled, no non-gratis subscription rows."],
];

summary.getRange("A3:B7").values = summaryRows;
summary.getRange("A3:A7").format.font = { bold: true };
summary.getRange("A3:B7").format.borders = { preset: "all", style: "thin", color: "#D9D9D9" };
summary.getRange("B3").setNumberFormat("yyyy-mm-dd hh:mm");
summary.getRange("A:A").format.columnWidthPx = 210;
summary.getRange("B:B").format.columnWidthPx = 620;

sheet.getRangeByIndexes(0, 0, 1, dataHeaders.length).values = [dataHeaders];
if (dataRows.length > 0) {
  sheet.getRangeByIndexes(1, 0, dataRows.length, dataHeaders.length).values = dataRows;
}

const usedRange = sheet.getRangeByIndexes(0, 0, Math.max(dataRows.length + 1, 2), dataHeaders.length);
usedRange.format.borders = { preset: "insideHorizontal", style: "thin", color: "#E5E7EB" };
sheet.getRangeByIndexes(0, 0, 1, dataHeaders.length).format.fill = { color: "#222222" };
sheet.getRangeByIndexes(0, 0, 1, dataHeaders.length).format.font = { bold: true, color: "#FFFFFF" };
sheet.getRangeByIndexes(0, 0, 1, dataHeaders.length).format.rowHeightPx = 28;
sheet.freezePanes.freezeRows(1);

const tableRange = `A1:M${Math.max(dataRows.length + 1, 2)}`;
const table = sheet.tables.add(tableRange, true, "FreeOnlySubscribers");
table.style = "TableStyleMedium2";
table.showFilterButton = true;

sheet.getRange("A:A").format.columnWidthPx = 300;
sheet.getRange("B:C").format.columnWidthPx = 145;
sheet.getRange("D:D").format.columnWidthPx = 210;
sheet.getRange("E:E").format.columnWidthPx = 130;
sheet.getRange("F:F").format.columnWidthPx = 315;
sheet.getRange("G:G").format.columnWidthPx = 125;
sheet.getRange("H:H").format.columnWidthPx = 120;
sheet.getRange("I:K").format.columnWidthPx = 145;
sheet.getRange("L:M").format.columnWidthPx = 160;
sheet.getRange("G:G").format.numberFormat = "yyyy-mm-dd";
sheet.getRange("L:M").format.numberFormat = "yyyy-mm-dd hh:mm";
sheet.getRange("A:M").format.wrapText = false;
sheet.getRange("A1:M1").format.wrapText = true;
sheet.getRange("A1:M1").format.rowHeightPx = 42;
sheet.getRange("H:H").format.columnWidthPx = 150;
sheet.getRange("K:K").format.columnWidthPx = 190;
sheet.getRange("L:M").format.columnWidthPx = 185;

const inspection = await workbook.inspect({
  kind: "table",
  range: "Free Only Subscribers!A1:M12",
  include: "values,formulas",
  tableMaxRows: 12,
  tableMaxCols: 13,
  maxChars: 4000,
});
console.log(inspection.ndjson);

const errors = await workbook.inspect({
  kind: "match",
  searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
  options: { useRegex: true, maxResults: 50 },
  summary: "final formula error scan",
  maxChars: 2000,
});
console.log(errors.ndjson);

const preview = await workbook.render({
  sheetName: "Free Only Subscribers",
  range: "A1:M20",
  scale: 1,
  format: "png",
});
await fs.mkdir(outputDir, { recursive: true });
await fs.writeFile(path.join(outputDir, "free-only-subscribers-preview.png"), new Uint8Array(await preview.arrayBuffer()));

const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(outputPath);

console.log(JSON.stringify({
  outputPath,
  generatedAt: now.toISOString(),
  includedSubscribers: freeOnly.length,
  fetchedSubscribers: subscribers.length,
  fetchedSubscriptions: subscriptions.length,
}));
