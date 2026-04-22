import os
import sys
import json
import subprocess
from pathlib import Path
from typing import Optional

from fastapi import FastAPI, HTTPException, BackgroundTasks
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field
import uvicorn

sys.path.insert(0, str(Path(__file__).parent))

from predictor import get_predictor, AccountingPredictor
from data_loader import load_all_training_data
from accounting_rules import validate_journal_entry, check_zatca_compliance

MODELS_DIR = Path(__file__).parent.parent / "models"

app = FastAPI(
    title="GuardianAI ML Engine",
    description="نظام التنبؤ المحاسبي الذكي — GITC International",
    version="1.0.0",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


class PredictRequest(BaseModel):
    description: str = Field(..., description="وصف المعاملة بالعربية أو الإنجليزية")
    transaction_type: str = Field("other", description="نوع المعاملة: invoice/receipt/expense/bank_statement/credit_note/other")
    amount: float = Field(0.0, description="المبلغ")
    tax_rate: float = Field(0.0, description="معدل الضريبة (مثال: 15.0)")
    currency: str = Field("SAR", description="العملة")
    has_vat_number: bool = Field(False, description="هل يوجد رقم ضريبي للمورد؟")


class ValidateRequest(BaseModel):
    debit_code: str
    credit_code: str
    debit_amount: float
    credit_amount: float
    transaction_type: str = "other"


class FeedbackRequest(BaseModel):
    description: str
    transaction_type: str
    amount: float
    correct_debit_code: str
    correct_credit_code: str
    tax_rate: float = 0.0
    currency: str = "SAR"


class ZatcaRequest(BaseModel):
    amount: float
    tax_rate: float
    has_vat_number: bool = False
    transaction_type: str = "invoice"


@app.get("/health")
def health():
    meta_exists = (MODELS_DIR / "model_meta.json").exists()
    return {
        "status": "ok",
        "models_trained": meta_exists,
        "models_dir": str(MODELS_DIR),
    }


@app.get("/model-info")
def model_info():
    meta_path = MODELS_DIR / "model_meta.json"
    if not meta_path.exists():
        raise HTTPException(status_code=404, detail="Models not trained yet. POST /train first.")
    with open(meta_path) as f:
        return json.load(f)


@app.post("/predict")
def predict(req: PredictRequest):
    try:
        predictor = get_predictor()
        result = predictor.predict(
            description=req.description,
            transaction_type=req.transaction_type,
            amount=req.amount,
            tax_rate=req.tax_rate,
            currency=req.currency,
            has_vat_number=req.has_vat_number,
        )
        return result
    except FileNotFoundError as e:
        raise HTTPException(status_code=503, detail=str(e))
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))


@app.post("/validate")
def validate(req: ValidateRequest):
    result = validate_journal_entry(
        req.debit_code, req.credit_code,
        req.debit_amount, req.credit_amount,
        req.transaction_type,
    )
    return result


@app.post("/zatca-check")
def zatca_check(req: ZatcaRequest):
    result = check_zatca_compliance(
        req.amount, req.tax_rate, req.has_vat_number, req.transaction_type
    )
    return result


@app.post("/feedback")
def submit_feedback(req: FeedbackRequest, background_tasks: BackgroundTasks):
    feedback_path = MODELS_DIR / "feedback_log.jsonl"
    entry = {
        "description": req.description,
        "transaction_type": req.transaction_type,
        "amount": req.amount,
        "correct_debit_code": req.correct_debit_code,
        "correct_credit_code": req.correct_credit_code,
        "tax_rate": req.tax_rate,
        "currency": req.currency,
        "source": "user_feedback",
    }
    with open(feedback_path, "a", encoding="utf-8") as f:
        f.write(json.dumps(entry, ensure_ascii=False) + "\n")
    feedback_count = sum(1 for _ in open(feedback_path))
    if feedback_count % 20 == 0:
        background_tasks.add_task(_retrain_in_background)
    return {"status": "saved", "feedback_count": feedback_count, "message": "شكراً! سيتم استخدام هذا التصحيح لتحسين النموذج."}


def _retrain_in_background():
    try:
        src_dir = Path(__file__).parent
        subprocess.run(
            ["python3", str(src_dir / "model_trainer.py")],
            cwd=str(src_dir),
            timeout=300,
        )
        global _predictor_instance
        from predictor import _predictor_instance
        _predictor_instance = None
        print("[ML API] Model retrained successfully.")
    except Exception as e:
        print(f"[ML API] Retrain failed: {e}")


@app.post("/train")
def train_models():
    try:
        src_dir = Path(__file__).parent
        result = subprocess.run(
            ["python3", str(src_dir / "model_trainer.py")],
            cwd=str(src_dir),
            capture_output=True,
            text=True,
            timeout=300,
        )
        if result.returncode != 0:
            raise HTTPException(status_code=500, detail=result.stderr)
        global _predictor_instance
        from predictor import _predictor_instance as pi
        _predictor_instance = None
        meta_path = MODELS_DIR / "model_meta.json"
        if meta_path.exists():
            with open(meta_path) as f:
                meta = json.load(f)
            return {"status": "trained", "meta": meta}
        return {"status": "trained", "output": result.stdout}
    except subprocess.TimeoutExpired:
        raise HTTPException(status_code=504, detail="Training timed out")
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))


@app.get("/analytics")
def analytics():
    try:
        df = load_all_training_data()
        type_dist = df["transaction_type"].value_counts().to_dict()
        source_dist = df["source"].value_counts().to_dict()
        top_debit = df["debit_account_code"].value_counts().head(10).to_dict()
        top_credit = df["credit_account_code"].value_counts().head(10).to_dict()
        return {
            "total_samples": len(df),
            "transaction_type_distribution": type_dist,
            "data_source_distribution": source_dist,
            "top_debit_accounts": top_debit,
            "top_credit_accounts": top_credit,
        }
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))


if __name__ == "__main__":
    port = int(os.getenv("ML_PORT", "5000"))
    uvicorn.run(app, host="0.0.0.0", port=port, log_level="info")
