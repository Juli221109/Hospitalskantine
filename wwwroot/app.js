const loginPanel = document.querySelector("#login-panel");
const appPanel = document.querySelector("#app-panel");
const patientPanel = document.querySelector("#patient-panel");
const canteenPanel = document.querySelector("#canteen-panel");
const daysList = document.querySelector("#days-list");
const countsList = document.querySelector("#counts-list");
const weeklyTotal = document.querySelector("#weekly-total");
const paymentMethodSection = document.querySelector("#payment-method-section");
const paymentMethodSelect = document.querySelector("#payment-method");
let currentUser = null;

async function api(path, options = {}) {
  const response = await fetch(path, {
    credentials: "same-origin",
    ...options,
    headers: { "Content-Type": "application/json", ...options.headers },
  });
  const result = response.status === 204 ? null : await response.json();
  if (!response.ok) {
    throw new Error(result?.error ?? "Anmodningen kunne ikke gennemføres.");
  }
  return result;
}

function setMessage(element, message = "") {
  element.textContent = message;
}

function showLogin() {
  currentUser = null;
  hideBookingQr();
  appPanel.hidden = true;
  loginPanel.hidden = false;
  document.querySelector("#username").focus();
}

function showApp(user) {
  currentUser = user;
  loginPanel.hidden = true;
  appPanel.hidden = false;
  const isCanteen = user.role === "canteen";
  patientPanel.hidden = isCanteen;
  canteenPanel.hidden = !isCanteen;
  document.querySelector("#role-label").textContent = isCanteen ? "Kantineoverblik" : "Din madplan";
  document.querySelector("#page-title").textContent = isCanteen ? "Køkkenets overblik" : "Velkommen til kantinen";
  document.querySelector("#page-description").textContent = isCanteen
    ? "Se antal bestilte måltider og betaling fra pårørende."
    : "Vælg måltider til den kommende uge.";
  document.querySelector("#account-label").textContent = user.patientId
    ? `Patient-ID ${user.patientId}`
    : user.username;
  if (isCanteen) {
    loadCounts();
  } else {
    loadChoices();
  }
}

function dateLabel(isoDate) {
  return new Intl.DateTimeFormat("da-DK", { weekday: "long", day: "numeric", month: "long" })
    .format(new Date(`${isoDate}T12:00:00`));
}

function createDayCard(day) {
  const card = document.createElement("article");
  card.className = "day-card";
  card.dataset.date = day.dato;

  const head = document.createElement("header");
  head.className = "day-head";
  const title = document.createElement("h3");
  title.textContent = dateLabel(day.dato);
  head.append(title);

  const menu = document.createElement("div");
  menu.className = "menu";
  menu.innerHTML = `<strong>Morgenmad:</strong> ${escapeHtml(day.menu.morgenmad)}<br><strong>Frokost:</strong> ${escapeHtml(day.menu.frokost)}<br><strong>Aftensmad:</strong> ${escapeHtml(day.menu.aftensmad)}`;

  const fields = document.createElement("div");
  fields.className = "meal-fields";
  for (const [meal, label, price] of [
    ["morgenmad", "Morgenmad", day.priser.morgenmad],
    ["frokost", "Frokost", day.priser.frokost],
    ["aftensmad", "Aftensmad", day.priser.aftensmad],
  ]) {
    const field = document.createElement("div");
    field.className = "meal-field";
    const id = `valg-${meal}-${day.dato}`;
    const inputLabel = document.createElement("label");
    inputLabel.htmlFor = id;
    inputLabel.innerHTML = `${label}<span>Pårørende ${price} kr</span>`;
    const select = document.createElement("select");
    select.id = id;
    select.dataset.meal = meal;
    select.dataset.price = String(price);
    select.required = true;
    for (const [value, text] of [["0", "Spiser ikke"], ["1", "Patient spiser (gratis)"], ["2", "Patient + 1 pårørende"]]) {
      const option = document.createElement("option");
      option.value = value;
      option.textContent = text;
      select.append(option);
    }
    const current = day[`${meal}Valg`];
    if (current !== null) select.value = String(current);
    select.addEventListener("change", updatePrices);
    field.append(inputLabel, select);
    fields.append(field);
  }

  const cost = document.createElement("p");
  cost.className = "day-cost";
  card.append(head, menu, fields, cost);
  updateDayCost(card);
  return card;
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (character) => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;",
  })[character]);
}

function updateDayCost(card) {
  const total = [...card.querySelectorAll("select")].reduce(
    (sum, select) => sum + (select.value === "2" ? Number(select.dataset.price) : 0),
    0,
  );
  card.querySelector(".day-cost").textContent = `Pårørendes betaling: ${total} kr`;
  return total;
}

function updatePrices() {
  const cards = [...daysList.querySelectorAll(".day-card")];
  const total = cards
    .reduce((sum, card) => sum + updateDayCost(card), 0);
  weeklyTotal.textContent = `Pårørendes betaling for ugen: ${total} kr`;

  const hasPayingRelative = cards.some((card) =>
    [...card.querySelectorAll("select")].some((select) => select.value === "2"));
  paymentMethodSection.hidden = !hasPayingRelative;
  paymentMethodSelect.required = hasPayingRelative;
  if (!hasPayingRelative) {
    paymentMethodSelect.value = "";
  }
}

async function loadChoices() {
  try {
    const { days } = await api("/api/choices");
    daysList.replaceChildren(...days.map(createDayCard));
    paymentMethodSelect.value = days.find((day) => day.betalingsmetode)?.betalingsmetode ?? "";
    updatePrices();
    showBookingQr(days.find((day) => day.bestillingsReference)?.bestillingsReference ?? null);
  } catch (error) {
    setMessage(document.querySelector("#choices-message"), error.message);
  }
}

function showBookingQr(reference) {
  const panel = document.querySelector("#booking-qr-panel");
  const image = document.querySelector("#booking-qr-image");
  if (!reference) {
    hideBookingQr();
    return;
  }

  document.querySelector("#booking-reference").textContent = `Bestillingsreference: ${reference}`;
  setMessage(document.querySelector("#booking-qr-message"));
  image.alt = `QR-kode for bestillingsreference ${reference}`;
  image.src = `/api/choices/qr/${encodeURIComponent(reference)}`;
  panel.hidden = false;
}

function hideBookingQr() {
  const panel = document.querySelector("#booking-qr-panel");
  if (!panel) return;
  panel.hidden = true;
  document.querySelector("#booking-qr-image").removeAttribute("src");
}

document.querySelector("#booking-qr-image").addEventListener("error", () => {
  setMessage(document.querySelector("#booking-qr-message"), "QR-koden kunne ikke indlæses. Gem dine valg igen, eller log ind på ny.");
});

function createCountCard(day) {
  const card = document.createElement("article");
  card.className = "day-card";
  const head = document.createElement("header");
  head.className = "day-head";
  const title = document.createElement("h3");
  title.textContent = dateLabel(day.dato);
  head.append(title);
  const menu = document.createElement("div");
  menu.className = "menu";
  menu.innerHTML = `<strong>Morgenmad:</strong> ${escapeHtml(day.menu.morgenmad)}<br><strong>Frokost:</strong> ${escapeHtml(day.menu.frokost)}<br><strong>Aftensmad:</strong> ${escapeHtml(day.menu.aftensmad)}`;

  const counts = document.createElement("div");
  counts.className = "meal-counts";
  for (const [meal, label, price] of [
    ["morgenmad", "Morgenmad", day.priser.morgenmad],
    ["frokost", "Frokost", day.priser.frokost],
    ["aftensmad", "Aftensmad", day.priser.aftensmad],
  ]) {
    const tally = day.maaltider[meal];
    const row = document.createElement("span");
    row.textContent = `${label}: ${tally.patienter} patienter gratis + ${tally.paarorende} pårørende × ${price} kr = ${day.belob[meal]} kr`;
    counts.append(row);
  }
  const paymentMethods = day.betalingsmetoder;
  const paymentSummary = document.createElement("span");
  paymentSummary.className = "payment-summary";
  paymentSummary.textContent = `Pårørendes valgte metode: MobilePay ${paymentMethods.mobilePay} · Kort ${paymentMethods.kort}`;
  counts.append(paymentSummary);
  card.append(head, menu, counts);
  return card;
}

async function loadCounts() {
  try {
    const { days } = await api("/api/counts");
    countsList.replaceChildren(...days.map(createCountCard));
  } catch (error) {
    setMessage(document.querySelector("#counts-message"), error.message);
  }
}

document.querySelector("#login-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const loginForm = event.currentTarget;
  const form = new FormData(loginForm);
  const message = document.querySelector("#login-message");
  setMessage(message);
  try {
    const { user } = await api("/api/login", {
      method: "POST",
      body: JSON.stringify({
        brugernavn: form.get("username"),
        adgangskode: form.get("password"),
      }),
    });
    loginForm.reset();
    showApp(user);
  } catch (error) {
    setMessage(message, error.message);
  }
});

document.querySelector("#choices-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const button = document.querySelector("#save-button");
  const message = document.querySelector("#choices-message");
  button.disabled = true;
  setMessage(message);
  try {
    const days = [...daysList.querySelectorAll(".day-card")].map((card) => {
      const get = (meal) => Number(card.querySelector(`[data-meal="${meal}"]`).value);
      return {
        dato: card.dataset.date,
        morgenmadValg: get("morgenmad"),
        frokostValg: get("frokost"),
        aftensmadValg: get("aftensmad"),
      };
    });
    const { reference } = await api("/api/choices", {
      method: "POST",
      body: JSON.stringify({
        days,
        betalingsmetode: paymentMethodSelect.value || null,
      }),
    });
    showBookingQr(reference);
    const paymentText = paymentMethodSelect.value
      ? ` Betalingsmetode ${paymentMethodSelect.value} er registreret; ingen betaling er gennemført.`
      : "";
    setMessage(message, `Dine valg er gemt.${paymentText}`);
  } catch (error) {
    setMessage(message, error.message);
  } finally {
    button.disabled = false;
  }
});

document.querySelector("#logout-button").addEventListener("click", async () => {
  try {
    await api("/api/logout", { method: "POST", body: "{}" });
    showLogin();
  } catch (error) {
    alert(error.message);
  }
});

api("/api/session")
  .then(({ user }) => user ? showApp(user) : showLogin())
  .catch(() => showLogin());
