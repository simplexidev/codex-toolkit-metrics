const statusElement = document.querySelector("#status");
const heroMeta = document.querySelector("#hero-meta");
const gateContainer = document.querySelector("#quality-gates");
const efficiencyContainer = document.querySelector("#efficiency-signals");
const viewContent = document.querySelector("#view-content");
const navItems = [...document.querySelectorAll(".nav-item")];

const metricCatalog = {
  "scenario-pass-rate": { label: "Scenario pass rate", view: "overview" },
  "regression-count": { label: "Open regressions", view: "history-regressions" },
  "capability-coverage": { label: "Capability coverage", view: "capability-coverage" },
  "skill-activation-precision": { label: "Activation precision", view: "skills" },
  "skill-activation-recall": { label: "Activation recall", view: "skills" },
  "skill-count": { label: "Published skills", view: "skills" },
  "custom-agent-count": { label: "Custom agents", view: "agents" },
  "delegation-success-rate": { label: "Delegation success", view: "agents" },
  "context-isolation-rate": { label: "Context isolation", view: "agents" },
  "model-policy-compliance": { label: "GPT-only compliance", view: "models" },
  "always-visible-tokens": { label: "Always-visible context", view: "token-efficiency" },
  "activation-visible-tokens": { label: "Activation context", view: "token-efficiency" },
  "maximum-possible-load-tokens": { label: "Maximum possible load", view: "token-efficiency" },
  "jev-bounded-coverage": { label: "Bounded judgment coverage", view: "jev" },
  "tool-success-rate": { label: "Tool success rate", view: "tooling" },
  "mean-routing-overlap": { label: "Mean routing overlap", view: "routing-delegation" },
  "maximum-routing-overlap": { label: "Maximum routing overlap", view: "routing-delegation" },
  "routing-precision": { label: "Routing precision", view: "routing-delegation" },
  "static-context-bytes": { label: "Static context", view: "static-cost" },
  "skill-trigger-tokens": { label: "Skill trigger surface", view: "static-cost" },
  "lazy-reference-tokens": { label: "Lazy references", view: "static-cost" },
  "agent-configuration-tokens": { label: "Agent configuration", view: "static-cost" },
  "agent-instruction-tokens": { label: "Agent instructions", view: "static-cost" },
  "delegation-rate": { label: "Delegation rate", view: "routing-delegation" }
};

const baselineMetricCatalog = {
  "dotnet-vanilla-pass-rate": "Vanilla .NET pass rate",
  "dotnet-upstream-pass-rate": "Upstream .NET pass rate",
  "agents-builtin-pass-rate": "Built-in agent pass rate",
  "agents-current-custom-pass-rate": "Current custom agent pass rate"
};
for (const [name, label] of Object.entries(baselineMetricCatalog)) {
  metricCatalog[name] = { label, view: "pre-optimization" };
}

const viewCopy = {
  overview: ["Overview", "The newest available value for every published measurement."],
  skills: ["Skills", "Activation quality and the size of the runtime skill surface."],
  agents: ["Agents", "Delegation outcomes, context isolation, and agent inventory."],
  "pre-optimization": ["Pre-optimization", "Reusable skill and agent baselines captured before the v2 optimization phases."],
  models: ["Models", "Policy compliance for OpenAI/GPT-only execution and judging."],
  "token-efficiency": ["Token Efficiency", "Context paid up front, on activation, and at maximum load."],
  jev: ["JEV", "Coverage of bounded semantic judgment; unavailable measurements remain explicit."],
  tooling: ["Tooling", "Reliability of deterministic tool use across evaluated scenarios."],
  "routing-delegation": ["Routing / Delegation", "Activation overlap, routing precision, and delegation behavior."],
  "history-regressions": ["History / Regressions", "Sanitized snapshots ordered by measurement time."],
  "static-cost": ["Static Cost", "Estimated context footprint from deterministic repository inventory."],
  "capability-coverage": ["Capability Coverage", "How much of the declared runtime surface has an evaluated scenario."]
};

const qualityGates = [
  ["scenario-pass-rate", ">=", 0.9],
  ["capability-coverage", ">=", 0.8],
  ["model-policy-compliance", ">=", 1],
  ["regression-count", "<=", 0]
];
const efficiencySignals = ["always-visible-tokens", "activation-visible-tokens", "mean-routing-overlap", "delegation-rate"];

let snapshots = [];
let latestMetrics = new Map();
let currentView = "overview";

function element(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function formatMetric(metric) {
  if (!metric) return "Not measured";
  if (metric.unit === "ratio") return new Intl.NumberFormat(undefined, { style: "percent", maximumFractionDigits: 1 }).format(metric.value);
  if (metric.unit === "bytes") {
    return new Intl.NumberFormat(undefined, { style: "unit", unit: "kilobyte", maximumFractionDigits: 1 }).format(metric.value / 1000);
  }
  const value = new Intl.NumberFormat().format(metric.value);
  if (metric.unit === "count") return value;
  if (metric.unit === "estimated-tokens") return `${value} est. tokens`;
  return `${value} ${metric.unit}`;
}

function metricLabel(name) {
  if (metricCatalog[name]?.label) return metricCatalog[name].label;
  const baselineArm = ["dotnet-vanilla", "dotnet-upstream", "agents-builtin", "agents-current-custom"]
    .find(prefix => name.startsWith(`${prefix}-`));
  if (baselineArm) {
    const arm = baselineArm.replaceAll("-", " ");
    return `${arm} · ${name.slice(baselineArm.length + 1).replaceAll("-", " ")}`;
  }
  return name.replaceAll("-", " ");
}

function metricCard(name, metric) {
  const card = element("article", "metric-card");
  const top = element("div", "metric-top");
  top.append(element("span", "metric-label", metricLabel(name)));
  if (metric?.kind) top.append(element("span", `kind kind-${metric.kind}`, metric.kind));
  card.append(top, element("strong", metric ? "metric-value" : "metric-value unavailable", formatMetric(metric)));
  card.append(element("p", "metric-detail", metric ? (metric.method || metric.direction.replaceAll("-", " ")) : "No approved aggregate has been published yet."));
  return card;
}

function gateCard([name, operator, threshold]) {
  const metric = latestMetrics.get(name);
  const passes = metric && (operator === ">=" ? metric.value >= threshold : metric.value <= threshold);
  const card = element("article", `gate ${metric ? (passes ? "gate-pass" : "gate-fail") : "gate-pending"}`);
  card.append(element("span", "gate-status", metric ? (passes ? "Pass" : "Fail") : "Pending"));
  card.append(element("h3", "gate-name", metricLabel(name)), element("strong", "gate-value", formatMetric(metric)));
  const unit = metric?.unit ?? (name.includes("count") ? "count" : "ratio");
  card.append(element("p", "gate-threshold", `Gate ${operator} ${formatMetric({ value: threshold, unit })}`));
  return card;
}

function renderShell() {
  const latest = snapshots.at(-1);
  const totalRuns = snapshots.reduce((sum, item) => sum + item.provenance.sourceRuns, 0);
  statusElement.textContent = `${snapshots.length} approved snapshots · updated ${new Date(latest.generatedAt).toLocaleDateString()}`;
  const meta = [
    ["Revision", latest.subject.revision.slice(0, 12)],
    ["Source runs", new Intl.NumberFormat().format(totalRuns)],
    ["Publication", snapshots.every(item => item.provenance.sanitized) ? "Sanitized" : "Rejected"]
  ];
  for (const [label, value] of meta) {
    const group = element("div", "meta-group");
    group.append(element("dt", "meta-label", label), element("dd", "meta-value", value));
    heroMeta.append(group);
  }
  qualityGates.forEach(gate => gateContainer.append(gateCard(gate)));
  efficiencySignals.forEach(name => efficiencyContainer.append(metricCard(name, latestMetrics.get(name))));
  renderView(currentView);
}

function renderHistory(container) {
  const chart = element("div", "history-chart");
  const rates = snapshots.map(item => item.metrics.find(metric => metric.name === "scenario-pass-rate")?.value ?? null);
  if (rates.filter(value => value !== null).length > 1) {
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("viewBox", "0 0 600 150");
    svg.setAttribute("role", "img");
    svg.setAttribute("aria-label", "Scenario pass rate history");
    const points = rates.map((value, index) => `${30 + index * (540 / Math.max(1, rates.length - 1))},${130 - (value ?? 0) * 110}`).join(" ");
    const line = document.createElementNS("http://www.w3.org/2000/svg", "polyline");
    line.setAttribute("points", points);
    line.setAttribute("class", "trend-line");
    svg.append(line);
    chart.append(svg);
  } else {
    chart.append(element("p", "empty-note", "A trend appears after two approved snapshots contain this metric."));
  }
  container.append(chart);

  const table = element("table", "history-table");
  const head = element("thead");
  const headerRow = element("tr");
  ["Measured", "Revision", "Scenarios", "Pass rate", "Approval"].forEach(label => headerRow.append(element("th", "", label)));
  head.append(headerRow);
  const body = element("tbody");
  [...snapshots].reverse().forEach(item => {
    const row = element("tr");
    const rate = item.metrics.find(metric => metric.name === "scenario-pass-rate");
    [new Date(item.generatedAt).toLocaleDateString(), item.subject.revision.slice(0, 12), `${item.scenarios.passed}/${item.scenarios.total}`, formatMetric(rate), item.provenance.approval]
      .forEach(value => row.append(element("td", "", value)));
    body.append(row);
  });
  table.append(head, body);
  const scroll = element("div", "table-scroll");
  scroll.append(table);
  container.append(scroll);
}

function renderView(view) {
  currentView = view;
  viewContent.replaceChildren();
  const [title, copy] = viewCopy[view];
  const heading = element("div", "view-heading");
  heading.append(element("p", "eyebrow", "Metric view"), element("h2", "", title), element("p", "view-copy", copy));
  viewContent.append(heading);
  if (view === "history-regressions") {
    renderHistory(viewContent);
    return;
  }
  const names = view === "overview"
    ? [...latestMetrics.keys()]
    : view === "pre-optimization"
      ? [...latestMetrics.keys()].filter(name => name.startsWith("dotnet-") || name.startsWith("agents-"))
    : Object.entries(metricCatalog).filter(([, definition]) => definition.view === view).map(([name]) => name);
  const grid = element("div", "metrics-grid");
  names.forEach(name => grid.append(metricCard(name, latestMetrics.get(name))));
  viewContent.append(grid);
}

for (const item of navItems) {
  item.addEventListener("click", () => {
    navItems.forEach(button => {
      const active = button === item;
      button.classList.toggle("is-active", active);
      if (active) button.setAttribute("aria-current", "page"); else button.removeAttribute("aria-current");
    });
    renderView(item.dataset.view);
    viewContent.focus({ preventScroll: true });
  });
}

fetch("./data/publication-manifest.json")
  .then(response => {
    if (!response.ok) throw new Error(`manifest HTTP ${response.status}`);
    return response.json();
  })
  .then(manifest => Promise.all(manifest.artifacts.map(path => fetch(`./data/${path}`).then(response => {
    if (!response.ok) throw new Error(`${path} HTTP ${response.status}`);
    return response.json();
  }))))
  .then(data => {
    snapshots = data.sort((left, right) => new Date(left.generatedAt) - new Date(right.generatedAt));
    for (const snapshot of snapshots) for (const metric of snapshot.metrics) latestMetrics.set(metric.name, metric);
    renderShell();
  })
  .catch(error => {
    statusElement.classList.add("error");
    statusElement.textContent = `Unable to load approved metrics: ${error.message}`;
    viewContent.append(element("p", "error-box", "The dashboard could not verify its published data manifest."));
  });
