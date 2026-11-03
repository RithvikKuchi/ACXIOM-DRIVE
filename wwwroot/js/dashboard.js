(() => {
  const source = document.getElementById('dashboard-data');
  if (!source || !window.Chart) return;
  const data = JSON.parse(source.textContent);
  Chart.defaults.font.family = 'Segoe UI, system-ui, -apple-system, sans-serif';
  Chart.defaults.color = '#748194';
  Chart.defaults.font.size = 11;
  const colors = ['#138f85', '#69b6be', '#7097ca', '#a89ac8', '#d3aa6a', '#c78080'];
  const formatMoney = value => new Intl.NumberFormat('en-IN', { maximumFractionDigits: 0 }).format(value);
  const common = { responsive: true, maintainAspectRatio: false, animation: { duration: 500 } };
  const axes = { x: { grid: { display: false }, border: { display: false } }, y: { beginAtZero: true, grid: { color: '#eef1f5' }, border: { display: false }, ticks: { callback: value => value >= 100000 ? (value / 100000) + 'L' : value >= 1000 ? (value / 1000) + 'k' : value } } };
  new Chart(document.getElementById('pipeline-chart'), {
    type: 'bar', data: { labels: data.pipeline.labels, datasets: [{ data: data.pipeline.values, backgroundColor: colors, borderRadius: 5, maxBarThickness: 40 }] },
    options: { ...common, plugins: { legend: { display: false }, tooltip: { callbacks: { label: context => ' INR ' + formatMoney(context.raw) } } }, scales: axes }
  });
  const leadValues = data.leads.values;
  new Chart(document.getElementById('leads-chart'), {
    type: 'doughnut', data: { labels: data.leads.labels, datasets: [{ data: leadValues.some(Boolean) ? leadValues : [1, 0, 0, 0, 0, 0], backgroundColor: leadValues.some(Boolean) ? colors : ['#e8edf2'], borderWidth: 3, borderColor: '#fff', hoverOffset: 3 }] },
    options: { ...common, cutout: '72%', plugins: { legend: { position: 'right', labels: { usePointStyle: true, pointStyle: 'circle', padding: 18, boxWidth: 7 } }, tooltip: { enabled: leadValues.some(Boolean) } } }
  });
  new Chart(document.getElementById('monthly-chart'), {
    type: 'line', data: { labels: data.monthly.labels, datasets: [{ label: 'Won', data: data.monthly.won, borderColor: '#138f85', backgroundColor: '#138f8512', fill: true, tension: 0.3, pointRadius: 3, borderWidth: 2 }, { label: 'Lost', data: data.monthly.lost, borderColor: '#c78080', backgroundColor: '#c7808010', tension: 0.3, pointRadius: 3, borderWidth: 2, borderDash: [4, 3] }] },
    options: { ...common, scales: axes, plugins: { legend: { position: 'bottom', labels: { usePointStyle: true, pointStyle: 'circle', padding: 18 } }, tooltip: { callbacks: { label: context => context.dataset.label + ': INR ' + formatMoney(context.raw) } } } }
  });
  const period = document.getElementById('period');
  const dates = [document.getElementById('from'), document.getElementById('to')];
  function updateDates() { dates.forEach(input => { input.disabled = period.value !== 'custom'; }); }
  period.addEventListener('change', updateDates); updateDates();
})();
