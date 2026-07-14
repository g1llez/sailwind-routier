let chartBuy;
let chartSell;
let selectedPort = null;
let selectedGood = null;
let selectedGoodName = null;
let allPorts = [];
let archipelagos = [];
let visiblePortIds = new Set();
let expandedArchipelagos = new Set();
let lastSnapshotId = null;
let refreshTimer = null;
let statusTicker = null;
let uiBound = false;
let lastStatus = null;
let lastRefreshedAt = null;

const FILTER_STORAGE_KEY = "routier.portFilter";
const EXPANDED_ARCHS_KEY = "routier.expandedArchs";
const REFRESH_ENABLED_KEY = "routier.autoRefresh";
const REFRESH_INTERVAL_KEY = "routier.refreshInterval";
const SLOT_STORAGE_KEY = "routier.saveSlot";
const CURRENCY_STORAGE_KEY = "routier.currency";
const PORT_STORAGE_KEY = "routier.port";
const GOOD_STORAGE_KEY = "routier.good";
const currencyNames = ["Al'Ankh Lions", "Emerald Dragons", "Aestrin Crowns", "Gold Lions"];
const currencyShort = ["Lions", "Dragons", "Crowns", "Gold Lions"];

let selectedCurrency = 0;
let selectedSaveSlot = 0;
let marketCurrency = null;
let lastGoodsData = null;
let lastCompareData = null;
let lastChartHistory = null;

function isMultiArchipelago() {
  const regions = new Set();
  for (const port of allPorts) {
    if (visiblePortIds.has(port.port_index)) regions.add(port.region);
  }
  return regions.size > 1;
}

function loadSelectedSaveSlot() {
  try {
    const raw = localStorage.getItem(SLOT_STORAGE_KEY);
    if (raw == null) return 0;
    const value = Number(raw);
    return value >= 0 && value <= 5 ? value : 0;
  } catch {
    return 0;
  }
}

function saveSelectedSaveSlot() {
  localStorage.setItem(SLOT_STORAGE_KEY, String(selectedSaveSlot));
}

function loadSelectedCurrency() {
  try {
    const raw = localStorage.getItem(CURRENCY_STORAGE_KEY);
    if (raw == null) return 0;
    const value = Number(raw);
    return value >= 0 && value <= 3 ? value : 0;
  } catch {
    return 0;
  }
}

function loadSavedPort() {
  try {
    const raw = localStorage.getItem(PORT_STORAGE_KEY);
    if (raw == null) return null;
    const value = Number(raw);
    return Number.isFinite(value) ? value : null;
  } catch {
    return null;
  }
}

function saveSelectedPort() {
  if (selectedPort != null) {
    localStorage.setItem(PORT_STORAGE_KEY, String(selectedPort));
  }
}

function loadSavedGood() {
  try {
    const raw = localStorage.getItem(GOOD_STORAGE_KEY);
    if (raw == null) return null;
    const value = Number(raw);
    return Number.isFinite(value) ? value : null;
  } catch {
    return null;
  }
}

function saveSelectedGood() {
  if (selectedGood != null) {
    localStorage.setItem(GOOD_STORAGE_KEY, String(selectedGood));
  }
}

function resolveGoodSelection(goods) {
  if (!goods.length) {
    selectedGood = null;
    selectedGoodName = null;
    return;
  }
  let row = selectedGood != null ? goods.find((g) => g.good_index === selectedGood) : null;
  if (!row) {
    const saved = loadSavedGood();
    row = saved != null ? goods.find((g) => g.good_index === saved) : null;
  }
  if (!row) row = goods[0];
  selectedGood = row.good_index;
  selectedGoodName = row.good_name;
  saveSelectedGood();
  updateChartHint();
  updateGoodMetaFromRow(row);
}

function formatWeight(lb) {
  if (lb == null) return null;
  const n = Number(lb);
  if (Number.isNaN(n)) return null;
  return Number.isInteger(n) ? n : Math.round(n * 10) / 10;
}

function updateGoodMetaFromRow(row) {
  const el = document.getElementById("chart-good-meta");
  if (!el) return;
  if (!row || (!row.size_description && row.weight_lb == null)) {
    el.hidden = true;
    el.textContent = "";
    return;
  }
  const parts = [];
  if (row.size_description) parts.push(row.size_description);
  const weight = formatWeight(row.weight_lb);
  if (weight != null) parts.push(`${weight} lb`);
  el.textContent = parts.join(" · ");
  el.hidden = parts.length === 0;
}

function getPortRegion(portIndex) {
  const port = allPorts.find((p) => p.port_index === portIndex);
  return port ? port.region : 0;
}

function needsConversionFee(portRegion, currencyIndex) {
  return portRegion !== currencyIndex;
}

function convertBuy(raw, priceIndex, withFee, exchangeFee) {
  const base = Number(priceIndex) * Number(raw);
  if (withFee) return Math.ceil(base * (1 + exchangeFee));
  return Math.round(base);
}

function convertSell(raw, priceIndex, withFee, exchangeFee) {
  const base = Number(priceIndex) * Number(raw);
  if (withFee) return Math.floor(base * (1 - exchangeFee));
  return Math.round(base);
}

function invertBuyRaw(display, priceIndex, withFee, exchangeFee) {
  const target = Number(display);
  for (let raw = 1; raw <= 50000; raw += 1) {
    if (convertBuy(raw, priceIndex, withFee, exchangeFee) === target) return raw;
  }
  return Math.max(1, Math.round(target / Number(priceIndex)));
}

function invertSellRaw(display, priceIndex, withFee, exchangeFee) {
  const target = Number(display);
  for (let raw = 1; raw <= 50000; raw += 1) {
    if (convertSell(raw, priceIndex, withFee, exchangeFee) === target) return raw;
  }
  return Math.max(1, Math.round(target / Number(priceIndex)));
}

function simPortRegion() {
  return simPort != null ? getPortRegion(simPort) : 0;
}

function simUsesConversionFee() {
  return needsConversionFee(simPortRegion(), selectedCurrency);
}

function displaySimBuy(raw) {
  const priceIndex = getMarketPriceIndex();
  if (priceIndex == null) return raw;
  return convertBuy(raw, priceIndex, simUsesConversionFee(), marketCurrency.exchange_fee);
}

function displaySimSell(raw) {
  const priceIndex = getMarketPriceIndex();
  if (priceIndex == null) return raw;
  return convertSell(raw, priceIndex, simUsesConversionFee(), marketCurrency.exchange_fee);
}

function observedDisplayToRaw(display, mode) {
  const priceIndex = getMarketPriceIndex();
  if (priceIndex == null) return Number(display);
  const withFee = simUsesConversionFee();
  const fee = marketCurrency?.exchange_fee ?? 0.01;
  if (mode === "sell") return invertSellRaw(display, priceIndex, withFee, fee);
  return invertBuyRaw(display, priceIndex, withFee, fee);
}

function storeMarketCurrency(data) {
  const priceByIndex = {};
  data.prices.forEach((row) => {
    priceByIndex[row.currency_index] = row.price_index;
  });
  marketCurrency = {
    snapshot_id: data.snapshot_id,
    priceByIndex,
    exchange_fee: data.exchange_fee ?? 0.01,
  };
}

function getMarketPriceIndex() {
  if (!marketCurrency) return null;
  return marketCurrency.priceByIndex[selectedCurrency];
}

function formatBuyCell(row, portRegion) {
  if (!row.available) return "—";
  const priceIndex = getMarketPriceIndex();
  if (priceIndex == null) return "—";
  const withFee = needsConversionFee(portRegion, selectedCurrency);
  return String(convertBuy(row.buy_raw, priceIndex, withFee, marketCurrency.exchange_fee));
}

function formatSellCell(row, portRegion) {
  const priceIndex = getMarketPriceIndex();
  if (priceIndex == null) return "—";
  const withFee = needsConversionFee(portRegion, selectedCurrency);
  return String(convertSell(row.sell_raw, priceIndex, withFee, marketCurrency.exchange_fee));
}

function updatePriceHeaders() {
  const buyTh = document.querySelector("#goods-table thead th:nth-child(2)");
  const sellTh = document.querySelector("#goods-table thead th:nth-child(3)");
  if (buyTh) buyTh.textContent = "Buy";
  if (sellTh) sellTh.textContent = "Sell";
}

function onCurrencyChanged() {
  localStorage.setItem(CURRENCY_STORAGE_KEY, String(selectedCurrency));
  syncCurrencySelects();
  updatePriceHeaders();
  updateChartHint();
  if (lastGoodsData) renderGoods(lastGoodsData);
  if (selectedPort !== null && selectedGood !== null) loadChart();
  loadDeals();
  updateSimObservedDefault();
  if (lastSimRawResult) renderSimResults(lastSimRawResult);
}

function syncCurrencySelects() {
  const marketSelect = document.getElementById("currency-select");
  const dealsSelect = document.getElementById("deals-currency-select");
  const simSelect = document.getElementById("sim-currency-select");
  if (marketSelect) marketSelect.value = String(selectedCurrency);
  if (dealsSelect) dealsSelect.value = String(selectedCurrency);
  if (simSelect) simSelect.value = String(selectedCurrency);
  const routeSelect = document.getElementById("route-currency-select");
  if (routeSelect) routeSelect.value = String(selectedCurrency);
}

function populateCurrencySelect() {
  const options = currencyNames.map((name, index) =>
    `<option value="${index}">${name}</option>`).join("");

  const select = document.getElementById("currency-select");
  if (select) {
    select.innerHTML = options;
    select.value = String(selectedCurrency);
    if (!select.dataset.bound) {
      select.addEventListener("change", () => {
        selectedCurrency = Number(select.value);
        onCurrencyChanged();
      });
      select.dataset.bound = "1";
    }
  }

  const dealsSelect = document.getElementById("deals-currency-select");
  if (dealsSelect) {
    dealsSelect.innerHTML = options;
    dealsSelect.value = String(selectedCurrency);
    if (!dealsSelect.dataset.bound) {
      dealsSelect.addEventListener("change", () => {
        selectedCurrency = Number(dealsSelect.value);
        onCurrencyChanged();
      });
      dealsSelect.dataset.bound = "1";
    }
  }

  const simSelect = document.getElementById("sim-currency-select");
  if (simSelect) {
    simSelect.innerHTML = options;
    simSelect.value = String(selectedCurrency);
    if (!simSelect.dataset.bound) {
      simSelect.addEventListener("change", () => {
        selectedCurrency = Number(simSelect.value);
        onCurrencyChanged();
      });
      simSelect.dataset.bound = "1";
    }
  }

  const routeSelect = document.getElementById("route-currency-select");
  if (routeSelect) {
    routeSelect.innerHTML = options;
    routeSelect.value = String(selectedCurrency);
    if (!routeSelect.dataset.bound) {
      routeSelect.addEventListener("change", () => {
        selectedCurrency = Number(routeSelect.value);
        onCurrencyChanged();
      });
      routeSelect.dataset.bound = "1";
    }
  }
}

async function fetchJson(url) {
  const sep = url.includes("?") ? "&" : "?";
  const response = await fetch(`${url}${sep}slot=${selectedSaveSlot}`);
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.error || response.statusText);
  }
  return response.json();
}

function loadSavedFilter() {
  try {
    const raw = localStorage.getItem(FILTER_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) return null;
    return new Set(parsed.map(Number));
  } catch {
    return null;
  }
}

function saveFilter() {
  localStorage.setItem(FILTER_STORAGE_KEY, JSON.stringify([...visiblePortIds]));
}

function loadExpandedArchipelagos() {
  try {
    const raw = localStorage.getItem(EXPANDED_ARCHS_KEY);
    if (!raw) return new Set();
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) return new Set();
    return new Set(parsed.map(Number));
  } catch {
    return new Set();
  }
}

function saveExpandedArchipelagos() {
  localStorage.setItem(EXPANDED_ARCHS_KEY, JSON.stringify([...expandedArchipelagos]));
}

function toggleArchipelagoExpanded(region) {
  if (expandedArchipelagos.has(region)) expandedArchipelagos.delete(region);
  else expandedArchipelagos.add(region);
  saveExpandedArchipelagos();
  const expanded = expandedArchipelagos.has(region);
  document.querySelectorAll(`.archipelago-group[data-region="${region}"]`).forEach((group) => {
    group.classList.toggle("expanded", expanded);
    const toggle = group.querySelector(".arch-toggle");
    if (toggle) toggle.setAttribute("aria-expanded", expanded ? "true" : "false");
  });
}

function setVisiblePorts(ids) {
  visiblePortIds = new Set(ids);
  saveFilter();
  renderAllArchipelagoFilters();
  renderPortSelect();
  if (selectedGood !== null) loadChart();
  loadDeals();
}

function renderAllArchipelagoFilters() {
  renderArchipelagoFilters("archipelago-filters", "arch");
  renderArchipelagoFilters("deals-archipelago-filters", "deals-arch");
  requestAnimationFrame(resizeCharts);
}

function getVisiblePorts() {
  return allPorts.filter((port) => visiblePortIds.has(port.port_index));
}

function formatRelativeTime(fromDate) {
  const seconds = Math.max(0, Math.floor((Date.now() - fromDate.getTime()) / 1000));
  if (seconds < 10) return "just now";
  if (seconds < 60) return `${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

function formatGameClock(gameTime) {
  if (gameTime == null || Number.isNaN(Number(gameTime))) return "";
  const totalMinutes = Math.round(Number(gameTime) * 60) % (24 * 60);
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  return `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}`;
}

function formatChartLabel(point) {
  const clock = formatGameClock(point.game_time);
  return clock ? `D${point.game_day} ${clock}` : `D${point.game_day}`;
}

function updateStatusText(status) {
  const statusEl = document.getElementById("status");
  if (!status.latest) {
    const slotLabel = status.save_slot != null ? `slot ${status.save_slot}` : "selected slot";
    statusEl.textContent = `No snapshots yet for ${slotLabel} — play Sailwind with Routier loaded.`;
    return;
  }
  const refreshed = lastRefreshedAt ? formatRelativeTime(lastRefreshedAt) : "just now";
  const gameClock = formatGameClock(status.latest.game_time);
  const dayPart = gameClock
    ? `day ${status.latest.game_day} · ${gameClock}`
    : `day ${status.latest.game_day}`;
  statusEl.textContent = `Snapshot #${status.latest.id} — ${dayPart} — updated ${refreshed}`;
}

function startStatusTicker() {
  if (statusTicker) return;
  statusTicker = setInterval(() => {
    if (lastStatus) updateStatusText(lastStatus);
  }, 1000);
}

function markRefreshed(status) {
  lastStatus = status;
  lastRefreshedAt = new Date();
  updateStatusText(status);
}

function renderCurrency(data) {
  storeMarketCurrency(data);

  const ratesBody = document.querySelector("#rates-table tbody");
  ratesBody.innerHTML = data.rates.map((row) => `
    <tr>
      <td>${currencyNames[row.sell_currency] || row.sell_currency}</td>
      <td>${currencyNames[row.buy_currency] || row.buy_currency}</td>
      <td>${Number(row.rate).toFixed(4)}</td>
      <td>${Number(row.rate_with_fee).toFixed(4)}</td>
    </tr>`).join("");

  const repBody = document.querySelector("#rep-table tbody");
  repBody.innerHTML = data.reputation.map((row) => `
    <tr>
      <td>${row.region_name}</td>
      <td>${row.reputation}</td>
      <td>${row.rep_level}</td>
      <td>${(row.retail_discount * 100).toFixed(1)}</td>
      <td>${(row.bulk_discount * 100).toFixed(1)}</td>
    </tr>`).join("");
}

function updateChartHint() {
  const hint = document.getElementById("chart-hint");
  if (selectedGoodName) {
    hint.textContent = `${selectedGoodName} — buy/sell history`;
  } else {
    hint.textContent = "Select a good from the table to view buy/sell over in-game time.";
  }
}

function selectGood(goodIndex, goodName, data) {
  selectedGood = goodIndex;
  selectedGoodName = goodName;
  saveSelectedGood();
  updateChartHint();
  const row = data.goods.find((item) => item.good_index === goodIndex);
  updateGoodMetaFromRow(row);
  loadChart();
  renderGoods(data);
}

function renderGoods(data) {
  lastGoodsData = data;
  const portRegion = getPortRegion(selectedPort);
  const body = document.querySelector("#goods-table tbody");
  body.innerHTML = data.goods.map((row) => `
    <tr class="${selectedGood === row.good_index ? "selected" : ""}" data-good="${row.good_index}" data-name="${row.good_name}">
      <td>${row.good_name}</td>
      <td>${formatBuyCell(row, portRegion)}</td>
      <td>${formatSellCell(row, portRegion)}</td>
      <td>${row.available ? (row.buy_qty ?? "—") : "—"}</td>
    </tr>`).join("");

  body.querySelectorAll("tr[data-good]").forEach((tr) => {
    tr.addEventListener("click", () => {
      selectGood(Number(tr.dataset.good), tr.dataset.name, data);
    });
  });
}

function archipelagoPortIds(group) {
  return group.ports.map((port) => port.port_index);
}

function isArchipelagoFullySelected(group) {
  return group.ports.every((port) => visiblePortIds.has(port.port_index));
}

function isArchipelagoPartiallySelected(group) {
  const selected = group.ports.filter((port) => visiblePortIds.has(port.port_index)).length;
  return selected > 0 && selected < group.ports.length;
}

function allPortIds() {
  return new Set(allPorts.map((port) => port.port_index));
}

function renderArchipelagoFilters(containerId, idPrefix) {
  const container = document.getElementById(containerId);
  if (!container) return;
  container.innerHTML = archipelagos.map((group) => {
    const groupId = `${idPrefix}-${group.region}`;
    const checked = isArchipelagoFullySelected(group) ? "checked" : "";
    const expanded = expandedArchipelagos.has(group.region);
    const islands = group.ports.map((port) => `
      <label>
        <input type="checkbox" class="island-filter" data-port="${port.port_index}"
          ${visiblePortIds.has(port.port_index) ? "checked" : ""}>
        <span>${port.port_name}</span>
      </label>`).join("");
    return `
      <div class="archipelago-group${expanded ? " expanded" : ""}" data-region="${group.region}">
        <div class="archipelago-header">
          <button type="button" class="arch-toggle" aria-expanded="${expanded ? "true" : "false"}"
            aria-label="${expanded ? "Hide" : "Show"} islands in ${group.name}"
            data-region="${group.region}"></button>
          <label class="archipelago-label">
            <input type="checkbox" class="archipelago-filter" id="${groupId}" ${checked}>
            <span>${group.name}</span>
          </label>
        </div>
        <div class="island-list">${islands}</div>
      </div>`;
  }).join("");

  archipelagos.forEach((group) => {
    const header = container.querySelector(`#${idPrefix}-${group.region}`);
    if (header) header.indeterminate = isArchipelagoPartiallySelected(group);
  });

  container.querySelectorAll(".arch-toggle").forEach((btn) => {
    btn.addEventListener("click", () => {
      toggleArchipelagoExpanded(Number(btn.dataset.region));
    });
  });

  container.querySelectorAll(".archipelago-filter").forEach((input) => {
    input.addEventListener("change", () => {
      const region = Number(input.closest(".archipelago-group").dataset.region);
      const group = archipelagos.find((item) => item.region === region);
      if (!group) return;
      const ids = new Set(visiblePortIds);
      archipelagoPortIds(group).forEach((portId) => {
        if (input.checked) ids.add(portId);
        else ids.delete(portId);
      });
      setVisiblePorts(ids);
    });
  });

  container.querySelectorAll(".island-filter").forEach((input) => {
    input.addEventListener("change", () => {
      const portId = Number(input.dataset.port);
      const ids = new Set(visiblePortIds);
      if (input.checked) ids.add(portId);
      else ids.delete(portId);
      setVisiblePorts(ids);
    });
  });
}

function renderPortSelect() {
  const select = document.getElementById("port-select");
  const visible = getVisiblePorts();
  const previousPort = selectedPort;
  select.innerHTML = visible.length
    ? visible.map((port) => `<option value="${port.port_index}">${port.port_name}</option>`).join("")
    : `<option value="">No islands selected</option>`;
  select.disabled = visible.length === 0;

  if (!visible.length) {
    selectedPort = null;
    selectedGood = null;
    selectedGoodName = null;
    document.querySelector("#goods-table tbody").innerHTML = "";
    updateChartHint();
    clearChart();
    return;
  }

  if (!visible.some((port) => port.port_index === selectedPort)) {
    const saved = loadSavedPort();
    if (saved != null && visible.some((port) => port.port_index === saved)) {
      selectedPort = saved;
    } else {
      selectedPort = visible[0].port_index;
    }
  }
  select.value = String(selectedPort);

  if (selectedPort !== previousPort || !lastGoodsData) {
    loadGoods();
  }
}

async function loadPorts(isRefresh = false) {
  const data = await fetchJson("/api/ports");
  allPorts = data.ports;
  archipelagos = data.archipelagos || [];

  if (!isRefresh) {
    const saved = loadSavedFilter();
    const validSaved = saved ? [...saved].filter((id) => allPortIds().has(id)) : [];
    visiblePortIds = validSaved.length ? new Set(validSaved) : allPortIds();
    expandedArchipelagos = loadExpandedArchipelagos();
  }

  renderAllArchipelagoFilters();
  renderPortSelect();
  renderSimPortSelect();
  renderRoutePortPick();
}

async function loadDeals() {
  const body = document.querySelector("#deals-table tbody");
  if (!body) return;
  if (visiblePortIds.size === 0) {
    body.innerHTML = `<tr><td colspan="9">Select at least one island</td></tr>`;
    return;
  }
  try {
    const ports = [...visiblePortIds].join(",");
    const data = await fetchJson(
      `/api/deals?currency_index=${selectedCurrency}&ports=${ports}&limit=10`
    );
    if (!data.deals.length) {
      body.innerHTML = `<tr><td colspan="9">No profitable deals in latest snapshot</td></tr>`;
      return;
    }
    body.innerHTML = data.deals.map((deal, index) => `
      <tr>
        <td>${index + 1}</td>
        <td>${deal.good_name}</td>
        <td>${deal.buy_port_name}</td>
        <td>${deal.buy_price}</td>
        <td>${deal.buy_qty ?? "—"}</td>
        <td>${deal.sell_port_name}</td>
        <td>${deal.sell_price}</td>
        <td class="profit-cell">+${deal.profit}</td>
        <td class="profit-cell">${deal.profit_pct != null ? `+${deal.profit_pct}%` : "—"}</td>
      </tr>`).join("");
  } catch (error) {
    body.innerHTML = `<tr><td colspan="9">${error.message}</td></tr>`;
  }
}

async function loadGoods() {
  if (selectedPort === null) return;
  const data = await fetchJson(`/api/goods?port_index=${selectedPort}`);
  resolveGoodSelection(data.goods);
  const active = data.goods.find((row) => row.good_index === selectedGood);
  updateGoodMetaFromRow(active);
  renderGoods(data);
  if (selectedGood !== null) await loadChart();
}

function formatDelta(delta, higherIsBetter) {
  if (delta == null) return { text: "n/a", cls: "neutral" };
  if (delta === 0) return { text: "= avg", cls: "neutral" };
  if (delta > 0) {
    return { text: `▲ +${delta}`, cls: higherIsBetter ? "above" : "below" };
  }
  return { text: `▼ ${delta}`, cls: higherIsBetter ? "below" : "above" };
}

function renderSideVsAvg(elId, parts) {
  const el = document.getElementById(elId);
  if (!parts.length) {
    el.hidden = true;
    el.innerHTML = "";
    return;
  }
  el.innerHTML = parts.join("");
  el.hidden = false;
}

function renderCurrentVsAvg(compare) {
  if (!compare?.current) {
    renderSideVsAvg("buy-vs-avg", []);
    renderSideVsAvg("sell-vs-avg", []);
    return;
  }
  const cur = compare.current;
  const archName = compare.archipelago_for_port?.name || "Archipelago";
  const multi = isMultiArchipelago();

  const buyParts = [];
  const sellParts = [];

  if (multi) {
    if (cur.buy != null && cur.buy_vs_archipelago != null) {
      const d = formatDelta(cur.buy_vs_archipelago, false);
      buyParts.push(`<span class="vs-badge ${d.cls}">vs ${archName} ${d.text}</span>`);
    }
    if (cur.sell != null && cur.sell_vs_archipelago != null) {
      const d = formatDelta(cur.sell_vs_archipelago, true);
      sellParts.push(`<span class="vs-badge ${d.cls}">vs ${archName} ${d.text}</span>`);
    }
  }
  if (cur.buy != null && cur.buy_vs_global != null) {
    const label = multi ? "vs global" : "vs avg";
    const d = formatDelta(cur.buy_vs_global, false);
    buyParts.push(`<span class="vs-badge ${d.cls}">${label} ${d.text}</span>`);
  }
  if (cur.sell != null && cur.sell_vs_global != null) {
    const label = multi ? "vs global" : "vs avg";
    const d = formatDelta(cur.sell_vs_global, true);
    sellParts.push(`<span class="vs-badge ${d.cls}">${label} ${d.text}</span>`);
  }

  renderSideVsAvg("buy-vs-avg", buyParts);
  renderSideVsAvg("sell-vs-avg", sellParts);
}

function renderComparePanel(compare) {
  const summary = document.getElementById("compare-summary");
  if (!compare) {
    summary.hidden = true;
    document.querySelector("#top-sell-table tbody").innerHTML = "";
    document.querySelector("#top-buy-table tbody").innerHTML = "";
    return;
  }
  summary.hidden = false;

  const benchBody = document.querySelector("#benchmark-table tbody");
  const rows = [];
  const multi = isMultiArchipelago();
  if (multi) {
    compare.archipelagos.forEach((arch) => {
      rows.push(`
        <tr>
          <td>${arch.name}</td>
          <td>${arch.buy_avg ?? "—"}</td>
          <td>${arch.sell_avg ?? "—"}</td>
          <td>${arch.sell_count}${arch.buy_count !== arch.sell_count ? ` (${arch.buy_count} buy)` : ""}</td>
        </tr>`);
    });
  }
  rows.push(`
    <tr>
      <td><strong>${multi ? "Global" : "Average"}</strong></td>
      <td>${compare.global.buy_avg ?? "—"}</td>
      <td>${compare.global.sell_avg ?? "—"}</td>
      <td>${compare.global.sell_count} islands</td>
    </tr>`);
  benchBody.innerHTML = rows.join("");

  const routeEl = document.getElementById("best-route");
  if (compare.best_route) {
    const r = compare.best_route;
    const profitPct = r.buy_price > 0
      ? Math.round((r.profit / r.buy_price) * 1000) / 10
      : null;
    const pctPart = profitPct != null ? ` (+${profitPct}%)` : "";
    routeEl.textContent =
      `Best deal: buy at ${r.buy_port_name} (${r.buy_price}) → sell at ${r.sell_port_name} (${r.sell_price}) = +${r.profit}${pctPart}`;
  } else {
    routeEl.textContent = "";
  }

  const sellBody = document.querySelector("#top-sell-table tbody");
  sellBody.innerHTML = compare.top_sell.map((row) => `
    <tr class="${row.is_current ? "current-port" : ""}">
      <td>${row.port_name}</td>
      <td>${row.sell}</td>
    </tr>`).join("") || `<tr><td colspan="2">No data</td></tr>`;

  const buyBody = document.querySelector("#top-buy-table tbody");
  buyBody.innerHTML = compare.top_buy.map((row) => `
    <tr class="${row.is_current ? "current-port" : ""}">
      <td>${row.port_name}</td>
      <td>${row.buy}</td>
    </tr>`).join("") || `<tr><td colspan="2">No buy available</td></tr>`;
}

function buildAvgLines(points, archipelagoName, side) {
  if (!points.length) return [];
  const multi = isMultiArchipelago();
  const globalKey = side === "buy" ? "avg_buy" : "avg_sell";
  const archKey = side === "buy" ? "arch_buy_avg" : "arch_sell_avg";
  const datasets = [];
  const makeLine = (label, dataKey, color) => ({
    label,
    data: points.map((p) => p[dataKey] ?? null),
    borderColor: color,
    borderDash: [6, 4],
    borderWidth: 1.5,
    pointRadius: 0,
    tension: 0,
    stepped: true,
    spanGaps: false,
  });

  if (multi) {
    if (points.some((p) => p[archKey] != null)) {
      const name = archipelagoName || "Archipelago";
      datasets.push(makeLine(`${name} avg`, archKey, side === "buy" ? "#c7b3ff" : "#ffd59e"));
    }
    if (points.some((p) => p[globalKey] != null)) {
      datasets.push(makeLine("Global avg", globalKey, side === "buy" ? "#9ecfff" : "#a8e6cf"));
    }
  } else if (points.some((p) => p[globalKey] != null)) {
    datasets.push(makeLine("Avg", globalKey, side === "buy" ? "#9ecfff" : "#a8e6cf"));
  }
  return datasets;
}

const chartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  plugins: { legend: { labels: { color: "#e8eef5", boxWidth: 12, font: { size: 11 } } } },
  scales: {
    x: { ticks: { color: "#8fa3b8", maxRotation: 45, font: { size: 10 } }, grid: { color: "#2a3a4f" } },
    y: {
      ticks: {
        color: "#8fa3b8",
        font: { size: 10 },
        stepSize: 1,
        precision: 0,
        callback: (value) => Math.round(value),
      },
      grid: { color: "#2a3a4f" },
    },
  },
};

async function loadCompare() {
  if (selectedPort === null || selectedGood === null || visiblePortIds.size === 0) {
    lastCompareData = null;
    renderComparePanel(null);
    renderCurrentVsAvg(null);
    document.getElementById("charts-stack").hidden = true;
    return null;
  }
  const ports = [...visiblePortIds].join(",");
  const data = await fetchJson(
    `/api/compare?good_index=${selectedGood}&currency_index=${selectedCurrency}&ports=${ports}&current_port=${selectedPort}`
  );
  lastCompareData = data;
  renderComparePanel(data);
  renderCurrentVsAvg(data);
  document.getElementById("charts-stack").hidden = false;
  return data;
}

function hideCompareUi() {
  lastCompareData = null;
  lastChartHistory = null;
  document.getElementById("compare-summary").hidden = true;
  document.getElementById("charts-stack").hidden = true;
  const meta = document.getElementById("chart-good-meta");
  if (meta) {
    meta.hidden = true;
    meta.textContent = "";
  }
  renderSideVsAvg("buy-vs-avg", []);
  renderSideVsAvg("sell-vs-avg", []);
}

function renderChartFromHistory(historyData) {
  const portRegion = getPortRegion(selectedPort);
  const fee = marketCurrency?.exchange_fee ?? 0.01;
  const withFee = needsConversionFee(portRegion, selectedCurrency);
  const points = historyData.points;

  const labels = points.map((p) => formatChartLabel(p));
  const buy = points.map((p) => {
    if (!p.available) return null;
    return convertBuy(p.buy_raw, p.price_index, withFee, fee);
  });
  const sell = points.map((p) =>
    convertSell(p.sell_raw, p.price_index, withFee, fee));

  const archName = historyData.archipelago_name
    || lastCompareData?.archipelago_for_port?.name;
  const buyRefs = buildAvgLines(points, archName, "buy");
  const sellRefs = buildAvgLines(points, archName, "sell");

  if (chartBuy) chartBuy.destroy();
  chartBuy = new Chart(document.getElementById("chart-buy"), {
    type: "line",
    data: {
      labels,
      datasets: [
        { label: "Buy", data: buy, borderColor: "#4db8ff", tension: 0.15, spanGaps: false },
        ...buyRefs,
      ],
    },
    options: chartOptions,
  });

  if (chartSell) chartSell.destroy();
  chartSell = new Chart(document.getElementById("chart-sell"), {
    type: "line",
    data: {
      labels,
      datasets: [
        { label: "Sell", data: sell, borderColor: "#7ddf9a", tension: 0.15 },
        ...sellRefs,
      ],
    },
    options: chartOptions,
  });
  requestAnimationFrame(resizeCharts);
}

async function loadChart() {
  if (selectedPort === null || selectedGood === null) return;
  const ports = [...visiblePortIds].join(",");
  const [historyData] = await Promise.all([
    fetchJson(
      `/api/history?port_index=${selectedPort}&good_index=${selectedGood}&currency_index=${selectedCurrency}&ports=${ports}&current_port=${selectedPort}`
    ),
    loadCompare(),
  ]);
  lastChartHistory = historyData;
  renderChartFromHistory(historyData);
}

function clearChart() {
  if (chartBuy) {
    chartBuy.destroy();
    chartBuy = null;
  }
  if (chartSell) {
    chartSell.destroy();
    chartSell = null;
  }
  hideCompareUi();
}

function resizeCharts() {
  if (chartBuy) chartBuy.resize();
  if (chartSell) chartSell.resize();
}

function setupLayoutObserver() {
  window.addEventListener("resize", resizeCharts);
  if (!window.ResizeObserver) return;
  const observer = new ResizeObserver(() => resizeCharts());
  const marketMain = document.querySelector(".market-main");
  const chartPanel = document.querySelector(".chart-panel");
  const chartsStack = document.getElementById("charts-stack");
  if (marketMain) observer.observe(marketMain);
  if (chartPanel) observer.observe(chartPanel);
  if (chartsStack) observer.observe(chartsStack);
}

function switchTab(tabId) {
  document.querySelectorAll(".tab").forEach((tab) => {
    const active = tab.dataset.tab === tabId;
    tab.classList.toggle("active", active);
    tab.setAttribute("aria-selected", active ? "true" : "false");
  });
  document.querySelectorAll(".tab-panel").forEach((panel) => {
    const active = panel.id === `tab-${tabId}`;
    panel.classList.toggle("active", active);
    panel.hidden = !active;
  });
  if (tabId === "deal") loadDeals();
  if (tabId === "sim") ensureSimTab();
  if (tabId === "route") ensureRouteTab();
  requestAnimationFrame(resizeCharts);
}

let simGoodsData = null;
let simPort = null;
let simGood = null;
let lastSimRawResult = null;

const SIM_PORT_KEY = "routier.simPort";
const SIM_GOOD_KEY = "routier.simGood";

function loadSimPort() {
  try {
    const raw = localStorage.getItem(SIM_PORT_KEY);
    if (raw == null) return null;
    const value = Number(raw);
    return Number.isFinite(value) ? value : null;
  } catch {
    return null;
  }
}

function saveSimPort() {
  if (simPort != null) localStorage.setItem(SIM_PORT_KEY, String(simPort));
}

function loadSimGood() {
  try {
    const raw = localStorage.getItem(SIM_GOOD_KEY);
    if (raw == null) return null;
    const value = Number(raw);
    return Number.isFinite(value) ? value : null;
  } catch {
    return null;
  }
}

function saveSimGood() {
  if (simGood != null) localStorage.setItem(SIM_GOOD_KEY, String(simGood));
}

function simMode() {
  return document.querySelector('input[name="sim-mode"]:checked')?.value || "buy";
}

function selectedSimGoodRow() {
  if (!simGoodsData || simGood == null) return null;
  return simGoodsData.find((row) => row.good_index === simGood) || null;
}

function renderSimPortSelect() {
  const select = document.getElementById("sim-port-select");
  if (!select) return;
  const visible = getVisiblePorts();
  select.innerHTML = visible.length
    ? visible.map((port) => `<option value="${port.port_index}">${port.port_name}</option>`).join("")
    : `<option value="">No islands selected</option>`;
  select.disabled = visible.length === 0;
  if (!visible.length) {
    simPort = null;
    simGoodsData = null;
    return;
  }
  if (!visible.some((port) => port.port_index === simPort)) {
    const saved = loadSimPort();
    if (saved != null && visible.some((port) => port.port_index === saved)) {
      simPort = saved;
    } else if (selectedPort != null && visible.some((port) => port.port_index === selectedPort)) {
      simPort = selectedPort;
    } else {
      simPort = visible[0].port_index;
    }
  }
  select.value = String(simPort);
}

function updateSimObservedDefault() {
  const row = selectedSimGoodRow();
  const input = document.getElementById("sim-observed-price");
  const hint = document.getElementById("sim-snapshot-hint");
  if (!row || !input) return;
  const mode = simMode();
  const display = mode === "sell"
    ? formatSellCell(row, simPortRegion())
    : formatBuyCell(row, simPortRegion());
  input.value = display === "—" ? "" : display;
  if (hint) {
    const cur = currencyShort[selectedCurrency] || "currency";
    const buyDisp = formatBuyCell(row, simPortRegion());
    const sellDisp = formatSellCell(row, simPortRegion());
    hint.hidden = false;
    hint.textContent =
      `Snapshot #${lastSnapshotId ?? "?"} — buy ${buyDisp}, sell ${sellDisp} ${cur}, ` +
      `qty ${row.buy_qty ?? "—"}` +
      (simUsesConversionFee() ? " (exchange fee applies)" : "");
  }
}

function renderSimGoodSelect() {
  const select = document.getElementById("sim-good-select");
  if (!select) return;
  if (!simGoodsData?.length) {
    select.innerHTML = `<option value="">No goods</option>`;
    select.disabled = true;
    return;
  }
  select.disabled = false;
  select.innerHTML = simGoodsData.map((row) =>
    `<option value="${row.good_index}">${row.good_name}</option>`
  ).join("");
  if (!simGoodsData.some((row) => row.good_index === simGood)) {
    const saved = loadSimGood();
    if (saved != null && simGoodsData.some((row) => row.good_index === saved)) {
      simGood = saved;
    } else {
      simGood = simGoodsData[0].good_index;
    }
  }
  select.value = String(simGood);
  saveSimGood();
  updateSimObservedDefault();
}

async function loadSimGoods() {
  if (simPort == null) return;
  const data = await fetchJson(`/api/goods?port_index=${simPort}`);
  simGoodsData = data.goods.filter((row) => row.available);
  renderSimGoodSelect();
}

function ensureSimTab() {
  renderSimPortSelect();
  if (simPort != null && !simGoodsData) {
    loadSimGoods().catch(showSimError);
  }
}

function showSimError(message) {
  const el = document.getElementById("sim-error");
  if (!el) return;
  el.hidden = false;
  el.textContent = message;
}

function clearSimError() {
  const el = document.getElementById("sim-error");
  if (!el) return;
  el.hidden = true;
  el.textContent = "";
}

function supplySourceLabel(source) {
  const labels = {
    explicit: "manual supply override",
    observed_buy: "calibrated from your first buy price",
    observed_sell: "calibrated from your first sell price",
    snapshot_buy_raw: "calibrated from snapshot buy price",
    snapshot_sell_raw: "calibrated from snapshot sell price",
    snapshot_supply: "snapshot supply (may be stale after trades)",
  };
  return labels[source] || source;
}

function renderSimResults(data) {
  lastSimRawResult = data;
  const wrap = document.getElementById("sim-results");
  const summary = document.getElementById("sim-summary");
  const body = document.querySelector("#sim-prices-table tbody");
  if (!wrap || !summary || !body) return;

  const isBuy = data.mode === "buy";
  const toDisplay = isBuy ? displaySimBuy : displaySimSell;
  const displayPrices = data.unit_prices.map((raw) => toDisplay(raw));
  const totalDisplay = displayPrices.reduce((sum, price) => sum + price, 0);
  const qty = isBuy ? data.quantity_bought : data.quantity_sold;
  const avg = qty ? Math.round((totalDisplay / qty) * 10) / 10 : "—";
  const cur = currencyShort[selectedCurrency] || "";

  summary.innerHTML = `
    <strong>${data.good_name}</strong> @ ${data.port_name} — ${isBuy ? "buy" : "sell"} × ${qty} (${cur})<br>
    Supply start: <strong>${Number(data.supply_start).toFixed(2)}</strong>
    (${supplySourceLabel(data.supply_source)})<br>
    Total: <strong>${totalDisplay}</strong> ${cur} · Avg: <strong>${avg}</strong> · Supply end: <strong>${Number(data.supply_end).toFixed(2)}</strong>`;

  let running = 0;
  body.innerHTML = displayPrices.map((price, index) => {
    running += price;
    return `<tr data-expected="${price}">
      <td>${index + 1}</td>
      <td>${price}</td>
      <td>${running}</td>
      <td><input type="number" class="sim-actual-price" min="0" step="1" aria-label="Your price unit ${index + 1}"></td>
    </tr>`;
  }).join("");

  body.querySelectorAll(".sim-actual-price").forEach((input) => {
    input.addEventListener("input", () => {
      const tr = input.closest("tr");
      const expected = Number(tr.dataset.expected);
      const actual = input.value === "" ? null : Number(input.value);
      tr.classList.remove("match", "mismatch");
      if (actual == null || Number.isNaN(actual)) return;
      tr.classList.add(actual === expected ? "match" : "mismatch");
    });
  });

  wrap.hidden = false;
}

async function runSim() {
  clearSimError();
  if (simPort == null || simGood == null) {
    showSimError("Select a port and good.");
    return;
  }
  const quantity = Number(document.getElementById("sim-quantity").value);
  if (!Number.isFinite(quantity) || quantity < 1) {
    showSimError("Quantity must be at least 1.");
    return;
  }
  const mode = simMode();
  const params = new URLSearchParams({
    mode,
    port_index: String(simPort),
    good_index: String(simGood),
    quantity: String(quantity),
  });
  const observed = document.getElementById("sim-observed-price").value.trim();
  if (observed !== "") {
    const rawObserved = observedDisplayToRaw(observed, mode);
    if (mode === "sell") params.set("observed_sell_raw", String(rawObserved));
    else params.set("observed_buy_raw", String(rawObserved));
  }
  const supplyOverride = document.getElementById("sim-supply-override").value.trim();
  if (supplyOverride !== "") params.set("supply_start", supplyOverride);

  try {
    const data = await fetchJson(`/api/simulate?${params}`);
    renderSimResults(data);
  } catch (error) {
    showSimError(error.message);
    document.getElementById("sim-results").hidden = true;
  }
}

function bindSimUi() {
  const portSelect = document.getElementById("sim-port-select");
  const goodSelect = document.getElementById("sim-good-select");
  const runBtn = document.getElementById("sim-run");
  if (!portSelect || !goodSelect || !runBtn) return;

  portSelect.addEventListener("change", async () => {
    simPort = Number(portSelect.value);
    saveSimPort();
    simGoodsData = null;
    await loadSimGoods();
  });

  goodSelect.addEventListener("change", () => {
    simGood = Number(goodSelect.value);
    saveSimGood();
    updateSimObservedDefault();
  });

  document.querySelectorAll('input[name="sim-mode"]').forEach((radio) => {
    radio.addEventListener("change", updateSimObservedDefault);
  });

  runBtn.addEventListener("click", () => runSim());
}

const ROUTE_STOPS_KEY = "routier.routeStops";
let routeStops = [];
let routeSelectedStop = -1;

function loadRouteStops() {
  try {
    const raw = localStorage.getItem(ROUTE_STOPS_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((stop) => stop && Number.isFinite(stop.port_index));
  } catch {
    return [];
  }
}

function saveRouteStops() {
  localStorage.setItem(ROUTE_STOPS_KEY, JSON.stringify(routeStops));
}

function renderRoutePortPick() {
  const select = document.getElementById("route-port-pick");
  if (!select || !allPorts.length) return;
  select.innerHTML = allPorts.map((port) =>
    `<option value="${port.port_index}">${port.port_name}</option>`
  ).join("");
}

function renderRouteStops() {
  const list = document.getElementById("route-stops");
  if (!list) return;
  if (!routeStops.length) {
    list.innerHTML = `<li class="route-empty">Add at least 2 stops in sailing order</li>`;
    routeSelectedStop = -1;
    return;
  }
  list.innerHTML = routeStops.map((stop, index) =>
    `<li class="${index === routeSelectedStop ? "selected" : ""}" data-index="${index}">${index + 1}. ${stop.port_name}</li>`
  ).join("");
  list.querySelectorAll("li[data-index]").forEach((item) => {
    item.addEventListener("click", () => {
      routeSelectedStop = Number(item.dataset.index);
      renderRouteStops();
    });
  });
}

function addRouteStop() {
  const pick = document.getElementById("route-port-pick");
  if (!pick) return;
  const portIndex = Number(pick.value);
  const port = allPorts.find((p) => p.port_index === portIndex);
  if (!port) return;
  routeStops.push({ port_index: port.port_index, port_name: port.port_name });
  routeSelectedStop = routeStops.length - 1;
  saveRouteStops();
  renderRouteStops();
}

function moveRouteStop(delta) {
  const next = routeSelectedStop + delta;
  if (routeSelectedStop < 0 || next < 0 || next >= routeStops.length) return;
  const tmp = routeStops[routeSelectedStop];
  routeStops[routeSelectedStop] = routeStops[next];
  routeStops[next] = tmp;
  routeSelectedStop = next;
  saveRouteStops();
  renderRouteStops();
}

function removeRouteStop() {
  if (routeSelectedStop < 0) return;
  routeStops.splice(routeSelectedStop, 1);
  routeSelectedStop = Math.min(routeSelectedStop, routeStops.length - 1);
  saveRouteStops();
  renderRouteStops();
}

function showRouteError(message) {
  const el = document.getElementById("route-error");
  if (!el) return;
  el.hidden = false;
  el.textContent = message;
}

function clearRouteError() {
  const el = document.getElementById("route-error");
  if (!el) return;
  el.hidden = true;
  el.textContent = "";
}

function formatSellSplit(deal) {
  return deal.sell_legs.map((leg) => `${leg.quantity} @ ${leg.port_name}`).join(", ");
}

function fmtSigned(n) {
  const v = Number(n) || 0;
  return (v < 0 ? "−" : "+") + Math.abs(v);
}

function renderRouteResults(data) {
  const wrap = document.getElementById("route-results");
  const summary = document.getElementById("route-summary");
  const body = document.querySelector("#route-deals-table tbody");
  if (!wrap || !summary || !body) return;

  const cur = currencyShort[data.currency_index] || "";
  const routeLabel = data.route_names.join(" → ");
  summary.innerHTML = `
    <strong>${routeLabel}</strong><br>
    Budget: <strong>${data.budget}</strong> ${cur} · Spent: <strong>${data.budget_spent}</strong> · Left: <strong>${data.budget_left}</strong><br>
    Planned profit: <strong>${fmtSigned(data.total_profit)}</strong> ${cur}` +
    (data.weight_used ? ` · Weight: <strong>${Math.round(data.weight_used)}</strong> lb` : "") +
    (data.volume_used ? ` · Volume: <strong>${Math.round(data.volume_used)}</strong> ft³` : "");

  if (!data.deals.length) {
    body.innerHTML = `<tr><td colspan="8">No profitable bulk deals on this route with current budget</td></tr>`;
  } else {
    const deals = [...data.deals].sort((a, b) => {
      const ai = data.route.indexOf(a.buy_port_index);
      const bi = data.route.indexOf(b.buy_port_index);
      return ai - bi;
    });
    body.innerHTML = deals.map((deal, index) => `
      <tr>
        <td>${index + 1}</td>
        <td>${deal.good_name}</td>
        <td>${deal.buy_port_name}</td>
        <td>${deal.quantity}</td>
        <td>${deal.buy_total}</td>
        <td>${formatSellSplit(deal)}</td>
        <td>${deal.sell_total}</td>
        <td class="profit-cell">${fmtSigned(deal.profit)}</td>
      </tr>`).join("");
  }
  wrap.hidden = false;
}

async function runRoutePlan() {
  clearRouteError();
  if (routeStops.length < 2) {
    showRouteError("Add at least 2 ports in route order.");
    return;
  }
  const budget = Number(document.getElementById("route-budget").value);
  if (!Number.isFinite(budget) || budget < 1) {
    showRouteError("Budget must be at least 1.");
    return;
  }
  const maxWeightRaw = document.getElementById("route-max-weight").value.trim();
  const maxVolumeRaw = document.getElementById("route-max-volume").value.trim();
  const params = new URLSearchParams({
    currency_index: String(selectedCurrency),
    ports: routeStops.map((s) => s.port_index).join(","),
    budget: String(budget),
  });
  if (maxWeightRaw !== "") {
    const maxWeight = Number(maxWeightRaw);
    if (!Number.isFinite(maxWeight) || maxWeight < 1) {
      showRouteError("Max weight must be a positive number.");
      return;
    }
    params.set("max_weight", String(maxWeight));
  }
  if (maxVolumeRaw !== "") {
    const maxVolume = Number(maxVolumeRaw);
    if (!Number.isFinite(maxVolume) || maxVolume < 1) {
      showRouteError("Max volume must be a positive number.");
      return;
    }
    params.set("max_volume", String(maxVolume));
  }

  try {
    const data = await fetchJson(`/api/route-plan?${params}`);
    renderRouteResults(data);
  } catch (error) {
    showRouteError(error.message);
    document.getElementById("route-results").hidden = true;
  }
}

function ensureRouteTab() {
  renderRoutePortPick();
  renderRouteStops();
}

function bindRouteUi() {
  const addBtn = document.getElementById("route-add-stop");
  const upBtn = document.getElementById("route-move-up");
  const downBtn = document.getElementById("route-move-down");
  const removeBtn = document.getElementById("route-remove-stop");
  const runBtn = document.getElementById("route-plan-run");
  if (!addBtn || !runBtn) return;

  addBtn.addEventListener("click", addRouteStop);
  upBtn.addEventListener("click", () => moveRouteStop(-1));
  downBtn.addEventListener("click", () => moveRouteStop(1));
  removeBtn.addEventListener("click", removeRouteStop);
  runBtn.addEventListener("click", () => runRoutePlan());
}

function getRefreshIntervalMs() {
  const select = document.getElementById("refresh-interval");
  return Number(select.value) * 1000;
}

function scheduleAutoRefresh() {
  if (refreshTimer) clearInterval(refreshTimer);
  const enabled = document.getElementById("auto-refresh").checked;
  localStorage.setItem(REFRESH_ENABLED_KEY, enabled ? "1" : "0");
  localStorage.setItem(REFRESH_INTERVAL_KEY, document.getElementById("refresh-interval").value);
  if (!enabled) return;
  refreshTimer = setInterval(() => refreshAll(true), getRefreshIntervalMs());
}

async function refreshAll(silent = false) {
  const statusEl = document.getElementById("status");
  try {
    const status = await fetchJson("/api/status");
    if (!status.latest) {
      markRefreshed(status);
      return;
    }

    const snapshotChanged = status.latest.id !== lastSnapshotId;
    markRefreshed(status);
    if (!snapshotChanged && silent) return;

    lastSnapshotId = status.latest.id;

    const currency = await fetchJson("/api/currency");
    renderCurrency(currency);
    await loadPorts(true);
    if (selectedPort !== null) await loadGoods();
    if (selectedPort !== null && selectedGood !== null) await loadChart();
    await loadDeals();
  } catch (error) {
    if (!silent) statusEl.textContent = error.message;
  }
}

function bindUi() {
  if (uiBound) return;
  uiBound = true;
  setupLayoutObserver();
  startStatusTicker();

  document.querySelectorAll(".tab").forEach((tab) => {
    tab.addEventListener("click", () => switchTab(tab.dataset.tab));
  });

  const select = document.getElementById("port-select");
  select.addEventListener("change", async () => {
    selectedPort = Number(select.value);
    saveSelectedPort();
    await loadGoods();
  });

  document.getElementById("filter-all").addEventListener("click", () => {
    setVisiblePorts(allPortIds());
  });
  document.getElementById("filter-none").addEventListener("click", () => {
    setVisiblePorts([]);
  });
  document.getElementById("deals-filter-all").addEventListener("click", () => {
    setVisiblePorts(allPortIds());
  });
  document.getElementById("deals-filter-none").addEventListener("click", () => {
    setVisiblePorts([]);
  });

  const autoRefresh = document.getElementById("auto-refresh");
  const refreshInterval = document.getElementById("refresh-interval");
  const savedEnabled = localStorage.getItem(REFRESH_ENABLED_KEY);
  const savedInterval = localStorage.getItem(REFRESH_INTERVAL_KEY);
  if (savedEnabled === "0") autoRefresh.checked = false;
  if (savedInterval) refreshInterval.value = savedInterval;

  autoRefresh.addEventListener("change", scheduleAutoRefresh);
  refreshInterval.addEventListener("change", scheduleAutoRefresh);
  document.getElementById("refresh-now").addEventListener("click", () => refreshAll(false));

  const saveSlotSelect = document.getElementById("save-slot-select");
  selectedSaveSlot = loadSelectedSaveSlot();
  saveSlotSelect.value = String(selectedSaveSlot);
  saveSlotSelect.addEventListener("change", async () => {
    selectedSaveSlot = Number(saveSlotSelect.value);
    saveSelectedSaveSlot();
    lastSnapshotId = null;
    await refreshAll(false);
  });

  scheduleAutoRefresh();
  bindSimUi();
  bindRouteUi();
  routeStops = loadRouteStops();
}

async function init() {
  const statusEl = document.getElementById("status");
  bindUi();
  selectedCurrency = loadSelectedCurrency();
  populateCurrencySelect();
  updatePriceHeaders();
  try {
    const status = await fetchJson("/api/status");
    if (!status.latest) {
      markRefreshed(status);
      return;
    }
    lastSnapshotId = status.latest.id;
    markRefreshed(status);
    const currency = await fetchJson("/api/currency");
    renderCurrency(currency);
    await loadPorts(false);
    await loadDeals();
  } catch (error) {
    statusEl.textContent = error.message;
  }
}

init();
