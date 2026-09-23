const status = document.querySelector("#status");
const summary = document.querySelector("#summary");
const metrics = document.querySelector("#metrics");

function card(label, value) {
  const element = document.createElement("article");
  element.className = "card";

  const labelElement = document.createElement("span");
  labelElement.className = "label";
  labelElement.textContent = label;

  const valueElement = document.createElement("strong");
  valueElement.className = "value";
  valueElement.textContent = value;

  element.append(labelElement, valueElement);
  return element;
}

function displayValue(metric) {
  if (metric.unit === "ratio") {
    return new Intl.NumberFormat(undefined, { style: "percent", maximumFractionDigits: 2 })
      .format(metric.value);
  }

  return `${new Intl.NumberFormat().format(metric.value)} ${metric.unit}`;
}

fetch("../data/public/static-cost-routing.json")
  .then((response) => {
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }

    return response.json();
  })
  .then((data) => {
    status.textContent = `Revision ${data.subject.revision.slice(0, 12)} · ${data.generatedAt}`;
    summary.append(
      card("Scenarios", new Intl.NumberFormat().format(data.scenarios.total)),
      card("Passed", new Intl.NumberFormat().format(data.scenarios.passed)),
      card("Source runs", new Intl.NumberFormat().format(data.provenance.sourceRuns))
    );

    for (const metric of data.metrics) {
      metrics.append(card(metric.name.replaceAll("-", " "), displayValue(metric)));
    }
  })
  .catch((error) => {
    status.className = "error";
    status.textContent = `Unable to load dashboard data: ${error.message}`;
  });
