import re
import numpy as np
import pandas as pd
from sklearn.preprocessing import LabelEncoder, StandardScaler
from sklearn.feature_extraction.text import TfidfVectorizer
import joblib
from pathlib import Path

MODELS_DIR = Path(__file__).parent.parent / "models"
MODELS_DIR.mkdir(exist_ok=True)

ARABIC_KEYWORDS = {
    "expense": ["مصروف", "دفع", "تكلفة", "رسوم", "فاتورة", "ايجار", "إيجار", "راتب", "رواتب", "كهرباء", "اتصالات", "ماء"],
    "revenue": ["إيراد", "مبيعات", "دخل", "عائد", "فاتورة مبيعات"],
    "asset": ["أصل", "معدات", "آلات", "مبنى", "سيارة", "موجودات"],
    "liability": ["دين", "قرض", "التزام", "مستحق", "آجل"],
    "tax": ["ضريبة", "زكاة", "ضريبة القيمة", "فاتورة", "15%"],
    "bank": ["بنك", "تحويل", "سحب", "إيداع", "كاش", "نقدي"],
    "depreciation": ["إهلاك", "استهلاك", "اندثار"],
    "payroll": ["راتب", "رواتب", "موظف", "أجر", "مكافأة"],
}

ENGLISH_KEYWORDS = {
    "expense": ["expense", "cost", "fee", "rent", "salary", "utilities", "electricity", "telecom"],
    "revenue": ["revenue", "sales", "income", "invoice", "receipt"],
    "asset": ["asset", "equipment", "machinery", "building", "vehicle", "property"],
    "liability": ["liability", "loan", "payable", "credit", "debt"],
    "tax": ["vat", "tax", "zatca", "15%", "zakat"],
    "bank": ["bank", "transfer", "cash", "deposit", "withdrawal"],
    "depreciation": ["depreciation", "amortization", "impairment"],
    "payroll": ["salary", "salaries", "payroll", "wages", "bonus"],
}

AMOUNT_RANGES = {"small": 0, "medium": 1, "large": 2, "xlarge": 3}
TRANSACTION_TYPES = ["invoice", "receipt", "expense", "bank_statement", "credit_note", "other"]


def extract_text_features(text: str) -> dict:
    text_lower = text.lower()
    features = {}
    for category, keywords in ARABIC_KEYWORDS.items():
        features[f"ar_{category}"] = int(any(kw in text_lower for kw in keywords))
    for category, keywords in ENGLISH_KEYWORDS.items():
        features[f"en_{category}"] = int(any(kw in text_lower for kw in keywords))
    features["text_length"] = len(text)
    features["has_arabic"] = int(bool(re.search(r'[\u0600-\u06FF]', text)))
    features["has_numbers"] = int(bool(re.search(r'\d', text)))
    features["has_percent"] = int("%" in text)
    features["has_sar"] = int("sar" in text_lower or "ريال" in text_lower)
    return features


def amount_to_range(amount: float) -> str:
    if amount < 500:
        return "small"
    elif amount < 5000:
        return "medium"
    elif amount < 50000:
        return "large"
    return "xlarge"


def build_feature_vector(
    description: str,
    transaction_type: str,
    amount: float = 0.0,
    tax_rate: float = 0.0,
    currency: str = "SAR",
) -> dict:
    features = extract_text_features(description)
    amount_range = amount_to_range(amount)
    features["amount_range"] = AMOUNT_RANGES.get(amount_range, 2)
    features["amount_log"] = float(np.log1p(amount))
    features["tax_rate"] = float(tax_rate)
    features["is_sar"] = int(currency.upper() == "SAR")
    features["is_usd"] = int(currency.upper() == "USD")
    type_idx = TRANSACTION_TYPES.index(transaction_type) if transaction_type in TRANSACTION_TYPES else 5
    for i, t in enumerate(TRANSACTION_TYPES):
        features[f"type_{t}"] = int(i == type_idx)
    return features


class FeatureEngineer:
    def __init__(self):
        self.tfidf = TfidfVectorizer(
            max_features=200,
            ngram_range=(1, 2),
            analyzer="char_wb",
            min_df=1,
        )
        self.label_encoder_debit = LabelEncoder()
        self.label_encoder_credit = LabelEncoder()
        self.scaler = StandardScaler()
        self.fitted = False
        self.feature_names = []

    def fit_transform(self, df: pd.DataFrame):
        descriptions = df["description"].fillna("").astype(str).tolist()
        tfidf_matrix = self.tfidf.fit_transform(descriptions).toarray()
        tfidf_cols = [f"tfidf_{i}" for i in range(tfidf_matrix.shape[1])]
        manual_features = []
        for _, row in df.iterrows():
            amount = float(row.get("amount", 0) or 0)
            tax_rate = float(row.get("tax_rate", 0) or 0)
            fv = build_feature_vector(
                description=str(row.get("description", "")),
                transaction_type=str(row.get("transaction_type", "other")),
                amount=amount,
                tax_rate=tax_rate,
                currency=str(row.get("currency", "SAR")),
            )
            manual_features.append(fv)
        manual_df = pd.DataFrame(manual_features)
        self.feature_names = list(manual_df.columns) + tfidf_cols
        manual_arr = self.scaler.fit_transform(manual_df.values.astype(float))
        X = np.hstack([manual_arr, tfidf_matrix])
        y_debit = self.label_encoder_debit.fit_transform(
            df["debit_account_code"].fillna("unknown").astype(str)
        )
        y_credit = self.label_encoder_credit.fit_transform(
            df["credit_account_code"].fillna("unknown").astype(str)
        )
        self.fitted = True
        return X, y_debit, y_credit

    def transform(self, description: str, transaction_type: str = "other",
                  amount: float = 0.0, tax_rate: float = 0.0, currency: str = "SAR"):
        tfidf_vec = self.tfidf.transform([description]).toarray()
        fv = build_feature_vector(description, transaction_type, amount, tax_rate, currency)
        manual_arr = self.scaler.transform(
            pd.DataFrame([fv]).reindex(
                columns=[c for c in self.feature_names if not c.startswith("tfidf_")],
                fill_value=0
            ).values.astype(float)
        )
        X = np.hstack([manual_arr, tfidf_vec])
        return X

    def save(self, path: Path = MODELS_DIR):
        joblib.dump(self.tfidf, path / "tfidf.pkl")
        joblib.dump(self.label_encoder_debit, path / "le_debit.pkl")
        joblib.dump(self.label_encoder_credit, path / "le_credit.pkl")
        joblib.dump(self.scaler, path / "scaler.pkl")
        joblib.dump(self.feature_names, path / "feature_names.pkl")

    def load(self, path: Path = MODELS_DIR):
        self.tfidf = joblib.load(path / "tfidf.pkl")
        self.label_encoder_debit = joblib.load(path / "le_debit.pkl")
        self.label_encoder_credit = joblib.load(path / "le_credit.pkl")
        self.scaler = joblib.load(path / "scaler.pkl")
        self.feature_names = joblib.load(path / "feature_names.pkl")
        self.fitted = True
