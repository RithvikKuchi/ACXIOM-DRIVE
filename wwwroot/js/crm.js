/* Native and unobtrusive validation share the same field-level message locations. */
(() => {
    'use strict';
    const form = document.getElementById('crm-form');
    if (!form) return;
    const field = name => form.elements.namedItem('Input.' + name);
    const kind = form.dataset.kind;
    const today = form.dataset.today;
    const create = form.dataset.create === 'true';
    const message = input => form.querySelector('[data-valmsg-for="' + input.name + '"]');
    const show = input => {
        const target = message(input);
        if (!target) return;
        if (!input.validity.valid) {
            target.textContent = input.validationMessage;
            target.classList.remove('field-validation-valid');
            target.classList.add('field-validation-error');
            input.classList.add('is-invalid');
            input.setAttribute('aria-invalid', 'true');
        } else {
            target.textContent = '';
            target.classList.remove('field-validation-error');
            target.classList.add('field-validation-valid');
            input.classList.remove('is-invalid');
            input.removeAttribute('aria-invalid');
        }
    };
    function rules(source) {
        if (kind === 'opportunities') {
            const stage = field('Stage');
            const status = field('Status');
            if (source === stage) {
                status.value = stage.value === 'Won' || stage.value === 'Lost' ? stage.value : 'Open';
            } else if (source === status) {
                if (status.value === 'Won' || status.value === 'Lost') stage.value = status.value;
                else if (stage.value === 'Won' || stage.value === 'Lost') stage.value = 'Qualification';
            }
            const active = status.value === 'Open';
            const date = field('ExpectedCloseDate');
            active ? date.setAttribute('min', today) : date.removeAttribute('min');
            const amount = Number(field('Amount').value);
            const probability = Number(field('Probability').value);
            const weighted = document.getElementById('weighted-pipeline');
            if (weighted) weighted.textContent = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(Number.isFinite(amount * probability) ? amount * probability / 100 : 0);
        }
        if (kind === 'followups') {
            const date = field('FollowUpDate');
            create || field('Status').value === 'Planned' ? date.setAttribute('min', today) : date.removeAttribute('min');
        }
        if (kind === 'followups') {
            const linked = ['CustomerId', 'LeadId', 'OpportunityId'].some(name => field(name)?.value);
            field('CustomerId').setCustomValidity(linked ? '' : 'Link at least one customer, lead or opportunity.');
        }
    }
    for (const input of form.querySelectorAll('input,select,textarea')) {
        input.addEventListener('change', () => { rules(input); show(input); });
        input.addEventListener('input', () => { rules(input); if (input.classList.contains('is-invalid')) show(input); });
        input.addEventListener('invalid', () => show(input));
    }
    // jQuery validation intentionally uses novalidate; explicitly check HTML constraints too.
    form.addEventListener('submit', event => {
        rules();
        const valid = form.checkValidity();
        if (!valid) {
            event.preventDefault();
            event.stopImmediatePropagation();
            for (const input of form.querySelectorAll('input,select,textarea')) show(input);
            form.querySelector(':invalid')?.focus();
        }
    }, true);
    rules();
})();
