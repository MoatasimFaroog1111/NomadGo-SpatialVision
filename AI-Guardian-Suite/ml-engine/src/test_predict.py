from predictor import get_predictor

p = get_predictor()

tests = [
    ('Equipment Rental', 'invoice', 14000, 15.0),
    ('Transportation', 'invoice', 977, 15.0),
    ('Building Material Sand', 'invoice', 1400, 15.0),
    ('Office Rent', 'invoice', 50000, 15.0),
    ('Salary payment', 'other', 25000, 0.0),
    ('Water and Electricity', 'expense', 5000, 0.0),
    ('فاتورة كهرباء', 'invoice', 2000, 15.0),
    ('دفع رواتب الموظفين', 'other', 30000, 0.0),
]

for desc, ttype, amount, tax in tests:
    r = p.predict(desc, ttype, amount, tax)
    mode = r.get('mode', 'unknown')
    conf = r.get('confidence', 0)

    if mode == 'single':
        dr = r.get('debit_account', {})
        cr = r.get('credit_account', {})
        dr_code = dr.get('code', '?')
        dr_name = dr.get('name', '')[:25]
        cr_code = cr.get('code', '?')
        cr_name = cr.get('name', '')[:25]
        print(f'{desc[:28]:28} [single] DR:{dr_code} ({dr_name}) CR:{cr_code} ({cr_name}) conf:{conf:.2f}')
    else:
        top_dr = r.get('top_debit_candidates', [{}])[0]
        top_cr = r.get('top_credit_candidates', [{}])[0]
        dr_code = top_dr.get('code', '?')
        cr_code = top_cr.get('code', '?')
        dr_name = top_dr.get('name', '')[:20]
        print(f'{desc[:28]:28} [multi]  DR:{dr_code} ({dr_name}) CR:{cr_code} conf:{conf:.2f}')
