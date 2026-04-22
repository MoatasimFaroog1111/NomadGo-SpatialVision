import json
from pathlib import Path
from typing import Optional

REFERENCE_DATA_PATH = Path(__file__).parent.parent.parent / "artifacts" / "api-server" / "src" / "lib" / "gitc-reference-data.json"

ACCOUNT_TYPE_NORMAL_BALANCE = {
    "Bank and Cash": "debit",
    "Current Assets": "debit",
    "Non-current Assets": "debit",
    "Expenses": "debit",
    "Cost of Revenue": "debit",
    "Current Liabilities": "credit",
    "Non-current Liabilities": "credit",
    "Equity": "credit",
    "Income": "credit",
    "Other Income": "credit",
    "Prepayments": "debit",
    "Receivable": "debit",
    "Payable": "credit",
}

INVALID_COMBINATIONS = [
    ("Income", "Income"),
    ("Expenses", "Expenses"),
    ("Bank and Cash", "Bank and Cash"),
]

TRANSACTION_TYPE_RULES = {
    "invoice": {
        "valid_debit_types": ["Current Assets", "Expenses", "Non-current Assets", "Receivable"],
        "valid_credit_types": ["Current Liabilities", "Income", "Payable", "Bank and Cash"],
        "description": "فاتورة: يجب أن يكون الدين في الأصول أو المصروفات",
    },
    "receipt": {
        "valid_debit_types": ["Bank and Cash", "Current Assets"],
        "valid_credit_types": ["Current Assets", "Income", "Receivable"],
        "description": "إيصال: يجب أن يكون الدين في النقدية أو البنك",
    },
    "expense": {
        "valid_debit_types": ["Expenses", "Cost of Revenue"],
        "valid_credit_types": ["Bank and Cash", "Current Liabilities", "Payable"],
        "description": "مصروف: يجب أن يكون الدين في حسابات المصروفات",
    },
    "bank_statement": {
        "valid_debit_types": ["Bank and Cash", "Current Assets"],
        "valid_credit_types": ["Bank and Cash", "Current Liabilities", "Income"],
        "description": "كشف بنكي: يجب أن يتضمن حسابات بنكية",
    },
    "credit_note": {
        "valid_debit_types": ["Income", "Current Liabilities"],
        "valid_credit_types": ["Current Assets", "Receivable"],
        "description": "إشعار دائن: عكس الفاتورة الأصلية",
    },
    "other": {
        "valid_debit_types": list(ACCOUNT_TYPE_NORMAL_BALANCE.keys()),
        "valid_credit_types": list(ACCOUNT_TYPE_NORMAL_BALANCE.keys()),
        "description": "معاملة عامة",
    },
}


def _load_accounts() -> dict:
    try:
        with open(REFERENCE_DATA_PATH) as f:
            data = json.load(f)
        return {a["code"]: a for a in data.get("accounts", [])}
    except Exception:
        return {}


_ACCOUNTS_CACHE = None


def get_account_info(code: str) -> Optional[dict]:
    global _ACCOUNTS_CACHE
    if _ACCOUNTS_CACHE is None:
        _ACCOUNTS_CACHE = _load_accounts()
    return _ACCOUNTS_CACHE.get(str(code))


def validate_journal_entry(
    debit_code: str,
    credit_code: str,
    debit_amount: float,
    credit_amount: float,
    transaction_type: str = "other",
) -> dict:
    errors = []
    warnings = []

    if abs(debit_amount - credit_amount) > 0.01:
        errors.append(f"القيد غير متوازن: الدين={debit_amount:.2f} الدائن={credit_amount:.2f}")

    if debit_code == credit_code:
        errors.append(f"لا يمكن أن يكون حساب الدين والدائن نفس الحساب: {debit_code}")

    debit_info = get_account_info(debit_code)
    credit_info = get_account_info(credit_code)

    if not debit_info:
        warnings.append(f"حساب الدين غير موجود في دليل الحسابات: {debit_code}")
    if not credit_info:
        warnings.append(f"حساب الدائن غير موجود في دليل الحسابات: {credit_code}")

    if debit_info and credit_info:
        debit_type = debit_info.get("type", "")
        credit_type = credit_info.get("type", "")

        for invalid_dr, invalid_cr in INVALID_COMBINATIONS:
            if invalid_dr in debit_type and invalid_cr in credit_type:
                errors.append(f"تركيبة غير صالحة: {debit_type} → {credit_type}")

        rules = TRANSACTION_TYPE_RULES.get(transaction_type, TRANSACTION_TYPE_RULES["other"])
        valid_debit = any(vt in debit_type for vt in rules["valid_debit_types"])
        valid_credit = any(vt in credit_type for vt in rules["valid_credit_types"])

        if not valid_debit:
            warnings.append(f"حساب الدين ({debit_type}) غير معتاد لنوع المعاملة '{transaction_type}'")
        if not valid_credit:
            warnings.append(f"حساب الدائن ({credit_type}) غير معتاد لنوع المعاملة '{transaction_type}'")

    return {
        "valid": len(errors) == 0,
        "errors": errors,
        "warnings": warnings,
        "debit_account": debit_info,
        "credit_account": credit_info,
    }


def apply_rules_to_prediction(
    debit_candidates: list,
    credit_candidates: list,
    transaction_type: str,
    amount: float,
) -> dict:
    rules = TRANSACTION_TYPE_RULES.get(transaction_type, TRANSACTION_TYPE_RULES["other"])
    filtered_debit = []
    for code, prob in debit_candidates:
        info = get_account_info(code)
        if info:
            account_type = info.get("type", "")
            if any(vt in account_type for vt in rules["valid_debit_types"]):
                filtered_debit.append((code, prob * 1.1, info))
            else:
                filtered_debit.append((code, prob * 0.7, info))
        else:
            filtered_debit.append((code, prob * 0.5, {}))

    filtered_credit = []
    for code, prob in credit_candidates:
        info = get_account_info(code)
        if info:
            account_type = info.get("type", "")
            if any(vt in account_type for vt in rules["valid_credit_types"]):
                filtered_credit.append((code, prob * 1.1, info))
            else:
                filtered_credit.append((code, prob * 0.7, info))
        else:
            filtered_credit.append((code, prob * 0.5, {}))

    filtered_debit.sort(key=lambda x: x[1], reverse=True)
    filtered_credit.sort(key=lambda x: x[1], reverse=True)

    return {
        "debit_candidates": filtered_debit[:3],
        "credit_candidates": filtered_credit[:3],
        "rule_applied": rules["description"],
    }


def check_zatca_compliance(
    amount: float,
    tax_rate: float,
    has_vat_number: bool,
    transaction_type: str,
) -> dict:
    issues = []
    notes = []

    if transaction_type in ["invoice", "expense"] and amount > 1000:
        if tax_rate == 0:
            issues.append("فاتورة بدون ضريبة قيمة مضافة — تحقق من الإعفاء")
        elif abs(tax_rate - 15.0) > 0.1:
            notes.append(f"معدل الضريبة {tax_rate}% — المعدل القياسي في السعودية 15%")

    if amount > 375000 and not has_vat_number:
        issues.append("المبلغ يتجاوز حد التسجيل الضريبي (375,000 ريال) — يجب التحقق من رقم الضريبة")

    if transaction_type == "invoice" and amount > 1000:
        notes.append("تأكد من وجود رقم الفاتورة التسلسلي وفق متطلبات فاتورة ZATCA")

    return {
        "compliant": len(issues) == 0,
        "issues": issues,
        "notes": notes,
    }
