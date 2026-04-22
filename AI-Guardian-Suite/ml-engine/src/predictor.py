import json
import numpy as np
import joblib
from pathlib import Path
from typing import Optional

from feature_engineering import FeatureEngineer
from accounting_rules import apply_rules_to_prediction, validate_journal_entry, check_zatca_compliance

MODELS_DIR = Path(__file__).parent.parent / "models"
CONFIDENCE_THRESHOLD = 0.60
TOP_K = 3


class AccountingPredictor:
    def __init__(self):
        self.fe = FeatureEngineer()
        self.models = {}
        self.meta = {}
        self.loaded = False

    def load(self):
        meta_path = MODELS_DIR / "model_meta.json"
        if not meta_path.exists():
            raise FileNotFoundError(f"Model meta not found at {meta_path}. Run train first.")
        with open(meta_path) as f:
            self.meta = json.load(f)
        self.fe.load(MODELS_DIR)
        best = self.meta.get("best_model", "xgboost")
        for model_name in ["xgboost", "lightgbm", "random_forest"]:
            dp = MODELS_DIR / f"{model_name}_debit.pkl"
            cp = MODELS_DIR / f"{model_name}_credit.pkl"
            if dp.exists() and cp.exists():
                self.models[model_name] = {
                    "debit": joblib.load(dp),
                    "credit": joblib.load(cp),
                }
        self.best_model = best
        self.loaded = True
        print(f"[Predictor] Loaded model: {best}")

    def _get_top_k_predictions(self, model, X, label_encoder, k=TOP_K):
        if hasattr(model, "predict_proba"):
            probs = model.predict_proba(X)[0]
            top_indices = np.argsort(probs)[::-1][:k]
            results = []
            for idx in top_indices:
                if idx < len(label_encoder.classes_):
                    code = label_encoder.classes_[idx]
                    prob = float(probs[idx])
                    results.append((code, prob))
            return results
        else:
            pred = model.predict(X)[0]
            code = label_encoder.classes_[pred]
            return [(code, 1.0)]

    def predict(
        self,
        description: str,
        transaction_type: str = "other",
        amount: float = 0.0,
        tax_rate: float = 0.0,
        currency: str = "SAR",
        has_vat_number: bool = False,
    ) -> dict:
        if not self.loaded:
            self.load()

        X = self.fe.transform(description, transaction_type, amount, tax_rate, currency)

        ensemble_debit = {}
        ensemble_credit = {}

        for model_name, model_pair in self.models.items():
            debit_preds = self._get_top_k_predictions(
                model_pair["debit"], X, self.fe.label_encoder_debit
            )
            credit_preds = self._get_top_k_predictions(
                model_pair["credit"], X, self.fe.label_encoder_credit
            )
            weight = 1.5 if model_name == self.best_model else 1.0
            for code, prob in debit_preds:
                ensemble_debit[code] = ensemble_debit.get(code, 0) + prob * weight
            for code, prob in credit_preds:
                ensemble_credit[code] = ensemble_credit.get(code, 0) + prob * weight

        total_d = sum(ensemble_debit.values()) or 1
        total_c = sum(ensemble_credit.values()) or 1
        debit_candidates = sorted(
            [(code, prob / total_d) for code, prob in ensemble_debit.items()],
            key=lambda x: x[1], reverse=True
        )[:TOP_K]
        credit_candidates = sorted(
            [(code, prob / total_c) for code, prob in ensemble_credit.items()],
            key=lambda x: x[1], reverse=True
        )[:TOP_K]

        rule_result = apply_rules_to_prediction(
            debit_candidates, credit_candidates, transaction_type, amount
        )

        best_debit_code, best_debit_prob, best_debit_info = rule_result["debit_candidates"][0]
        best_credit_code, best_credit_prob, best_credit_info = rule_result["credit_candidates"][0]

        overall_confidence = (best_debit_prob + best_credit_prob) / 2

        validation = validate_journal_entry(
            best_debit_code, best_credit_code,
            amount, amount,
            transaction_type
        )

        zatca = check_zatca_compliance(amount, tax_rate, has_vat_number, transaction_type)

        suggested_description = _suggest_description(description, transaction_type, best_debit_info, best_credit_info)
        suggested_category = _suggest_category(transaction_type, best_debit_info)

        if overall_confidence >= CONFIDENCE_THRESHOLD:
            return {
                "mode": "single",
                "confidence": round(overall_confidence, 4),
                "debit_account": {
                    "code": best_debit_code,
                    "name": best_debit_info.get("name", best_debit_code),
                    "type": best_debit_info.get("type", ""),
                    "confidence": round(best_debit_prob, 4),
                },
                "credit_account": {
                    "code": best_credit_code,
                    "name": best_credit_info.get("name", best_credit_code),
                    "type": best_credit_info.get("type", ""),
                    "confidence": round(best_credit_prob, 4),
                },
                "suggested_description": suggested_description,
                "suggested_category": suggested_category,
                "validation": validation,
                "zatca_compliance": zatca,
                "rule_applied": rule_result["rule_applied"],
                "top_debit_candidates": [
                    {"code": c, "name": i.get("name", c), "confidence": round(p, 4)}
                    for c, p, i in rule_result["debit_candidates"]
                ],
                "top_credit_candidates": [
                    {"code": c, "name": i.get("name", c), "confidence": round(p, 4)}
                    for c, p, i in rule_result["credit_candidates"]
                ],
            }
        else:
            return {
                "mode": "multi",
                "confidence": round(overall_confidence, 4),
                "message": "الثقة منخفضة — يُعرض أفضل 3 خيارات",
                "top_debit_candidates": [
                    {"code": c, "name": i.get("name", c), "confidence": round(p, 4)}
                    for c, p, i in rule_result["debit_candidates"]
                ],
                "top_credit_candidates": [
                    {"code": c, "name": i.get("name", c), "confidence": round(p, 4)}
                    for c, p, i in rule_result["credit_candidates"]
                ],
                "suggested_description": suggested_description,
                "suggested_category": suggested_category,
                "validation": validation,
                "zatca_compliance": zatca,
                "rule_applied": rule_result["rule_applied"],
            }


def _suggest_description(description: str, transaction_type: str, debit_info: dict, credit_info: dict) -> str:
    type_labels = {
        "invoice": "فاتورة",
        "receipt": "إيصال",
        "expense": "مصروف",
        "bank_statement": "كشف بنكي",
        "credit_note": "إشعار دائن",
        "other": "قيد محاسبي",
    }
    label = type_labels.get(transaction_type, "قيد محاسبي")
    debit_name = debit_info.get("name", "")
    credit_name = credit_info.get("name", "")
    if debit_name and credit_name:
        return f"{label}: {description} — دين {debit_name} / دائن {credit_name}"
    return f"{label}: {description}"


def _suggest_category(transaction_type: str, debit_info: dict) -> str:
    debit_type = debit_info.get("type", "")
    if "Expenses" in debit_type or "Cost" in debit_type:
        return "مصروفات تشغيلية"
    elif "Assets" in debit_type:
        return "أصول"
    elif "Income" in debit_type:
        return "إيرادات"
    elif "Bank" in debit_type or "Cash" in debit_type:
        return "نقدية وبنوك"
    elif "Liabilities" in debit_type:
        return "التزامات"
    return {
        "invoice": "مشتريات",
        "receipt": "مقبوضات",
        "expense": "مصروفات",
        "bank_statement": "بنوك",
        "credit_note": "مردودات",
    }.get(transaction_type, "عام")


_predictor_instance = None


def get_predictor() -> AccountingPredictor:
    global _predictor_instance
    if _predictor_instance is None:
        _predictor_instance = AccountingPredictor()
        _predictor_instance.load()
    return _predictor_instance
