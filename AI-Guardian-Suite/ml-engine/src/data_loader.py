import os
import json
import pandas as pd
import numpy as np
import psycopg2
from pathlib import Path

REFERENCE_DATA_PATH = Path(__file__).parent.parent.parent / "artifacts" / "api-server" / "src" / "lib" / "gitc-reference-data.json"
REAL_ACCOUNTS_PATH = Path(__file__).parent.parent / "data" / "real_accounts.json"
REAL_TRAINING_PATH = Path(__file__).parent.parent / "data" / "real_training_data.csv"

DB_URL = os.getenv("DATABASE_URL", "postgresql://guardian:guardian123@localhost:5432/guardian_db")


def get_db_connection():
    import psycopg2
    from urllib.parse import urlparse
    p = urlparse(DB_URL)
    return psycopg2.connect(
        dbname=p.path.lstrip("/"),
        user=p.username,
        password=p.password,
        host=p.hostname,
        port=p.port or 5432,
    )


def load_reference_accounts() -> pd.DataFrame:
    if REAL_ACCOUNTS_PATH.exists():
        with open(REAL_ACCOUNTS_PATH, encoding="utf-8") as f:
            acc_dict = json.load(f)
        rows = list(acc_dict.values())
        df = pd.DataFrame(rows)
        df["code"] = df["code"].astype(str).str.strip()
        df["name"] = df["name"].astype(str).str.strip()
        df["type"] = df["type"].astype(str).str.strip()
        return df
    with open(REFERENCE_DATA_PATH) as f:
        data = json.load(f)
    accounts = data.get("accounts", [])
    df = pd.DataFrame(accounts)
    df["code"] = df["code"].astype(str).str.strip()
    df["name"] = df["name"].astype(str).str.strip()
    df["type"] = df["type"].astype(str).str.strip()
    return df


def load_reference_partners() -> pd.DataFrame:
    with open(REFERENCE_DATA_PATH) as f:
        data = json.load(f)
    partners = data.get("partners", [])
    df = pd.DataFrame(partners)
    return df


def load_transactions_from_db() -> pd.DataFrame:
    try:
        conn = get_db_connection()
        query = """
            SELECT
                t.id,
                t.type,
                t.status,
                t.supplier,
                t.invoice_number,
                t.invoice_date,
                t.currency,
                CAST(t.total_amount AS FLOAT) as total_amount,
                CAST(t.tax_amount AS FLOAT) as tax_amount,
                t.created_at,
                d.file_name,
                d.file_type,
                d.source,
                d.classification_label,
                CAST(d.classification_confidence AS FLOAT) as classification_confidence,
                d.extracted_data,
                d.raw_content
            FROM transactions t
            LEFT JOIN documents d ON d.id = t.document_id
            ORDER BY t.created_at DESC
        """
        df = pd.read_sql(query, conn)
        conn.close()
        return df
    except Exception as e:
        print(f"[DataLoader] DB load failed: {e}")
        return pd.DataFrame()


def load_supplier_memory_from_db() -> pd.DataFrame:
    try:
        conn = get_db_connection()
        query = """
            SELECT
                supplier_key,
                supplier_name,
                account_code,
                account_name,
                journal_name,
                CAST(tax_rate AS FLOAT) as tax_rate,
                currency,
                invoice_count,
                CAST(average_amount AS FLOAT) as average_amount,
                is_verified,
                user_corrections
            FROM supplier_memory
            ORDER BY invoice_count DESC
        """
        df = pd.read_sql(query, conn)
        conn.close()
        return df
    except Exception as e:
        print(f"[DataLoader] Supplier memory load failed: {e}")
        return pd.DataFrame()


def load_documents_from_db() -> pd.DataFrame:
    try:
        conn = get_db_connection()
        query = """
            SELECT
                id,
                file_name,
                file_type,
                source,
                status,
                classification_label,
                CAST(classification_confidence AS FLOAT) as classification_confidence,
                extracted_data,
                raw_content,
                created_at
            FROM documents
            WHERE status NOT IN ('failed', 'pending')
            ORDER BY created_at DESC
        """
        df = pd.read_sql(query, conn)
        conn.close()
        return df
    except Exception as e:
        print(f"[DataLoader] Documents load failed: {e}")
        return pd.DataFrame()


CANONICAL_ACCOUNT_MAPPINGS = [
    {
        "descriptions": ["شراء بضاعة بالآجل", "purchase inventory on credit", "مشتريات بضاعة", "شراء مواد", "فاتورة مشتريات"],
        "debit_code": "500001", "credit_code": "200001",
        "transaction_type": "invoice", "tax_rate": 15.0, "amount_range": "large",
    },
    {
        "descriptions": ["دفع إيجار", "pay rent expense", "إيجار مكتب", "إيجار مستودع", "إيجار شهري"],
        "debit_code": "500010", "credit_code": "100002",
        "transaction_type": "expense", "tax_rate": 15.0, "amount_range": "large",
    },
    {
        "descriptions": ["تسجيل فاتورة مبيعات", "record sales invoice", "فاتورة بيع", "مبيعات", "إيراد مبيعات"],
        "debit_code": "100020", "credit_code": "400001",
        "transaction_type": "invoice", "tax_rate": 15.0, "amount_range": "large",
    },
    {
        "descriptions": ["استلام دفعة من عميل", "receive customer payment", "تحصيل عميل", "دفعة عميل"],
        "debit_code": "100002", "credit_code": "100020",
        "transaction_type": "receipt", "tax_rate": 0.0, "amount_range": "large",
    },
    {
        "descriptions": ["قيد ضريبة القيمة المضافة", "VAT entry", "ضريبة القيمة المضافة", "ضريبة 15%", "زكاة وضريبة"],
        "debit_code": "100030", "credit_code": "200010",
        "transaction_type": "other", "tax_rate": 15.0, "amount_range": "medium",
    },
    {
        "descriptions": ["قسط إهلاك الأصول", "depreciation entry", "استهلاك أصول", "إهلاك سنوي", "اندثار"],
        "debit_code": "500020", "credit_code": "100010",
        "transaction_type": "expense", "tax_rate": 0.0, "amount_range": "medium",
    },
    {
        "descriptions": ["دفع رواتب الموظفين", "pay employee salaries", "رواتب شهرية", "أجور موظفين", "مسير رواتب"],
        "debit_code": "500030", "credit_code": "100002",
        "transaction_type": "expense", "tax_rate": 0.0, "amount_range": "xlarge",
    },
    {
        "descriptions": ["شراء أصل ثابت", "purchase fixed asset", "شراء معدات", "شراء آلات", "أصل ثابت جديد"],
        "debit_code": "100010", "credit_code": "100002",
        "transaction_type": "invoice", "tax_rate": 15.0, "amount_range": "xlarge",
    },
    {
        "descriptions": ["دفع فاتورة كهرباء", "pay electricity bill", "فاتورة كهرباء", "كهرباء شهرية"],
        "debit_code": "500040", "credit_code": "100002",
        "transaction_type": "expense", "tax_rate": 15.0, "amount_range": "small",
    },
    {
        "descriptions": ["دفع فاتورة اتصالات", "pay telecom bill", "فاتورة هاتف", "اتصالات شهرية", "إنترنت"],
        "debit_code": "500050", "credit_code": "100002",
        "transaction_type": "expense", "tax_rate": 15.0, "amount_range": "small",
    },
    {
        "descriptions": ["تسوية حساب بنكي", "bank reconciliation", "كشف حساب بنكي", "تسوية بنكية"],
        "debit_code": "100002", "credit_code": "100002",
        "transaction_type": "bank_statement", "tax_rate": 0.0, "amount_range": "large",
    },
    {
        "descriptions": ["إصدار إشعار دائن", "issue credit note", "إشعار دائن", "مردود مبيعات", "رد بضاعة"],
        "debit_code": "400001", "credit_code": "100020",
        "transaction_type": "credit_note", "tax_rate": 15.0, "amount_range": "medium",
    },
    {
        "descriptions": ["دفع مصاريف سفر", "travel expenses", "مصاريف تنقل", "بدل سفر"],
        "debit_code": "500060", "credit_code": "100002",
        "transaction_type": "expense", "tax_rate": 0.0, "amount_range": "small",
    },
    {
        "descriptions": ["دفع فاتورة مياه", "water bill", "فاتورة مياه", "مياه شهرية"],
        "debit_code": "500040", "credit_code": "100002",
        "transaction_type": "expense", "tax_rate": 0.0, "amount_range": "small",
    },
    {
        "descriptions": ["قرض بنكي", "bank loan", "اقتراض من البنك", "تسهيل بنكي"],
        "debit_code": "100002", "credit_code": "200020",
        "transaction_type": "other", "tax_rate": 0.0, "amount_range": "xlarge",
    },
]


def build_synthetic_training_data(accounts_df: pd.DataFrame) -> pd.DataFrame:
    rows = []
    augmentation_prefixes = [
        "", "تم ", "تسجيل ", "قيد ", "معالجة ",
        "process ", "record ", "post ", "entry for ", "transaction: ",
    ]
    for mapping in CANONICAL_ACCOUNT_MAPPINGS:
        for desc in mapping["descriptions"]:
            for prefix in augmentation_prefixes:
                augmented = prefix + desc
                rows.append({
                    "description": augmented,
                    "description_en": augmented,
                    "transaction_type": mapping["transaction_type"],
                    "amount_range": mapping["amount_range"],
                    "tax_rate": mapping["tax_rate"],
                    "debit_account_code": mapping["debit_code"],
                    "credit_account_code": mapping["credit_code"],
                    "debit_account_type": "",
                    "credit_account_type": "",
                    "confidence": 0.92,
                    "source": "synthetic",
                })
    return pd.DataFrame(rows)


def _UNUSED_build_synthetic_training_data_old(accounts_df: pd.DataFrame) -> pd.DataFrame:
    scenarios = [
        {"description": "شراء بضاعة بالآجل", "description_en": "Purchase inventory on credit",
         "debit_account_type": "Current Assets", "credit_account_type": "Current Liabilities",
         "transaction_type": "invoice", "amount_range": "large", "tax_rate": 15.0},
        {"description": "دفع إيجار", "description_en": "Pay rent expense",
         "debit_account_type": "Expenses", "credit_account_type": "Bank and Cash",
         "transaction_type": "expense", "amount_range": "large", "tax_rate": 15.0},
        {"description": "تسجيل فاتورة مبيعات", "description_en": "Record sales invoice",
         "debit_account_type": "Current Assets", "credit_account_type": "Income",
         "transaction_type": "invoice", "amount_range": "large", "tax_rate": 15.0},
        {"description": "استلام دفعة من عميل", "description_en": "Receive customer payment",
         "debit_account_type": "Bank and Cash", "credit_account_type": "Current Assets",
         "transaction_type": "receipt", "amount_range": "large", "tax_rate": 0.0},
        {"description": "قيد ضريبة القيمة المضافة", "description_en": "VAT entry",
         "debit_account_type": "Current Assets", "credit_account_type": "Current Liabilities",
         "transaction_type": "other", "amount_range": "medium", "tax_rate": 15.0},
        {"description": "قسط إهلاك الأصول", "description_en": "Depreciation entry",
         "debit_account_type": "Expenses", "credit_account_type": "Non-current Assets",
         "transaction_type": "expense", "amount_range": "medium", "tax_rate": 0.0},
        {"description": "دفع رواتب الموظفين", "description_en": "Pay employee salaries",
         "debit_account_type": "Expenses", "credit_account_type": "Bank and Cash",
         "transaction_type": "expense", "amount_range": "xlarge", "tax_rate": 0.0},
        {"description": "شراء أصل ثابت", "description_en": "Purchase fixed asset",
         "debit_account_type": "Non-current Assets", "credit_account_type": "Bank and Cash",
         "transaction_type": "invoice", "amount_range": "xlarge", "tax_rate": 15.0},
        {"description": "دفع فاتورة كهرباء", "description_en": "Pay electricity bill",
         "debit_account_type": "Expenses", "credit_account_type": "Bank and Cash",
         "transaction_type": "expense", "amount_range": "small", "tax_rate": 15.0},
        {"description": "دفع فاتورة اتصالات", "description_en": "Pay telecom bill",
         "debit_account_type": "Expenses", "credit_account_type": "Bank and Cash",
         "transaction_type": "expense", "amount_range": "small", "tax_rate": 15.0},
        {"description": "تسوية حساب بنكي", "description_en": "Bank reconciliation",
         "debit_account_type": "Bank and Cash", "credit_account_type": "Bank and Cash",
         "transaction_type": "bank_statement", "amount_range": "large", "tax_rate": 0.0},
        {"description": "إصدار إشعار دائن", "description_en": "Issue credit note",
         "debit_account_type": "Income", "credit_account_type": "Current Assets",
         "transaction_type": "credit_note", "amount_range": "medium", "tax_rate": 15.0},
    ]

    rows = []
    for s in scenarios:
        debit_accounts = accounts_df[accounts_df["type"].str.contains(
            s["debit_account_type"], case=False, na=False
        )]["code"].tolist()[:5]
        credit_accounts = accounts_df[accounts_df["type"].str.contains(
            s["credit_account_type"], case=False, na=False
        )]["code"].tolist()[:5]

        if not debit_accounts:
            debit_accounts = ["100002"]
        if not credit_accounts:
            credit_accounts = ["200001"]

        for dr in debit_accounts:
            for cr in credit_accounts:
                if dr != cr:
                    rows.append({
                        "description": s["description"],
                        "description_en": s["description_en"],
                        "transaction_type": s["transaction_type"],
                        "amount_range": s["amount_range"],
                        "tax_rate": s["tax_rate"],
                        "debit_account_code": dr,
                        "credit_account_code": cr,
                        "debit_account_type": s["debit_account_type"],
                        "credit_account_type": s["credit_account_type"],
                        "confidence": 0.85,
                        "source": "synthetic",
                    })

    return pd.DataFrame(rows)


def load_real_odoo_training_data() -> pd.DataFrame:
    if not REAL_TRAINING_PATH.exists():
        return pd.DataFrame()
    df = pd.read_csv(REAL_TRAINING_PATH, encoding="utf-8-sig")
    df = df.dropna(subset=["description", "debit_account_code", "credit_account_code"])
    df = df[df["debit_account_code"].astype(str).str.len() > 0]
    df = df[df["credit_account_code"].astype(str).str.len() > 0]
    df["source"] = "real_odoo"
    return df


def load_all_training_data() -> pd.DataFrame:
    accounts_df = load_reference_accounts()
    db_transactions = load_transactions_from_db()
    supplier_memory = load_supplier_memory_from_db()
    synthetic = build_synthetic_training_data(accounts_df)
    real_odoo = load_real_odoo_training_data()

    frames = [synthetic]
    if not real_odoo.empty:
        frames.append(real_odoo)

    if not db_transactions.empty and "type" in db_transactions.columns:
        db_rows = []
        for _, row in db_transactions.iterrows():
            extracted = row.get("extracted_data") or {}
            if isinstance(extracted, str):
                try:
                    extracted = json.loads(extracted)
                except Exception:
                    extracted = {}
            amount = row.get("total_amount", 0) or 0
            if amount < 500:
                amount_range = "small"
            elif amount < 5000:
                amount_range = "medium"
            elif amount < 50000:
                amount_range = "large"
            else:
                amount_range = "xlarge"

            db_rows.append({
                "description": extracted.get("description", row.get("supplier", "")),
                "description_en": extracted.get("description", ""),
                "transaction_type": row.get("type", "other"),
                "amount_range": amount_range,
                "tax_rate": float(row.get("tax_amount", 0) or 0),
                "debit_account_code": extracted.get("debitAccount", ""),
                "credit_account_code": extracted.get("creditAccount", ""),
                "debit_account_type": "",
                "credit_account_type": "",
                "confidence": float(row.get("classification_confidence", 0.7) or 0.7),
                "source": "db",
            })
        if db_rows:
            frames.append(pd.DataFrame(db_rows))

    if not supplier_memory.empty:
        mem_rows = []
        for _, row in supplier_memory.iterrows():
            if row.get("account_code"):
                mem_rows.append({
                    "description": row.get("supplier_name", ""),
                    "description_en": row.get("supplier_name", ""),
                    "transaction_type": "invoice",
                    "amount_range": "large",
                    "tax_rate": float(row.get("tax_rate", 15.0) or 15.0),
                    "debit_account_code": row.get("account_code", ""),
                    "credit_account_code": "200001",
                    "debit_account_type": "Expenses",
                    "credit_account_type": "Current Liabilities",
                    "confidence": 0.95 if row.get("is_verified") else 0.80,
                    "source": "memory",
                })
        if mem_rows:
            frames.append(pd.DataFrame(mem_rows))

    combined = pd.concat(frames, ignore_index=True)
    combined = combined.dropna(subset=["description", "transaction_type"])
    combined = combined[combined["debit_account_code"].astype(str).str.len() > 0]
    combined = combined[combined["credit_account_code"].astype(str).str.len() > 0]
    combined = combined.drop_duplicates(subset=["description", "debit_account_code", "credit_account_code"])

    return combined
