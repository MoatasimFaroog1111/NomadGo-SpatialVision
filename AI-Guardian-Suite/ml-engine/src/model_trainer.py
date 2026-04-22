import json
import numpy as np
import pandas as pd
import joblib
from pathlib import Path
from sklearn.model_selection import train_test_split
from sklearn.metrics import accuracy_score, f1_score, precision_score, recall_score
from sklearn.ensemble import RandomForestClassifier
from lightgbm import LGBMClassifier

from data_loader import load_all_training_data
from feature_engineering import FeatureEngineer

MODELS_DIR = Path(__file__).parent.parent / "models"
MODELS_DIR.mkdir(exist_ok=True)


def evaluate_model(model, X_test, y_test, label: str) -> dict:
    y_pred = model.predict(X_test)
    acc = accuracy_score(y_test, y_pred)
    f1 = f1_score(y_test, y_pred, average="weighted", zero_division=0)
    prec = precision_score(y_test, y_pred, average="weighted", zero_division=0)
    rec = recall_score(y_test, y_pred, average="weighted", zero_division=0)
    print(f"[{label}] Accuracy={acc:.3f} F1={f1:.3f} Precision={prec:.3f} Recall={rec:.3f}")
    return {"accuracy": acc, "f1": f1, "precision": prec, "recall": rec}


def train_models():
    print("[Trainer] Loading training data...")
    df = load_all_training_data()
    print(f"[Trainer] Total samples: {len(df)}")
    print(f"[Trainer] Sources: {df['source'].value_counts().to_dict()}")

    fe = FeatureEngineer()
    X, y_debit, y_credit = fe.fit_transform(df)
    print(f"[Trainer] Feature matrix shape: {X.shape}")
    print(f"[Trainer] Unique debit classes: {len(np.unique(y_debit))}")
    print(f"[Trainer] Unique credit classes: {len(np.unique(y_credit))}")

    X_train, X_test, yd_train, yd_test, yc_train, yc_test = train_test_split(
        X, y_debit, y_credit, test_size=0.2, random_state=42
    )

    models_debit = {
        "lightgbm": LGBMClassifier(
            n_estimators=200,
            max_depth=6,
            learning_rate=0.1,
            random_state=42,
            n_jobs=-1,
            verbose=-1,
        ),
        "random_forest": RandomForestClassifier(
            n_estimators=150,
            max_depth=10,
            random_state=42,
            n_jobs=-1,
        ),
    }

    models_credit = {
        "lightgbm": LGBMClassifier(
            n_estimators=200,
            max_depth=6,
            learning_rate=0.1,
            random_state=42,
            n_jobs=-1,
            verbose=-1,
        ),
        "random_forest": RandomForestClassifier(
            n_estimators=150,
            max_depth=10,
            random_state=42,
            n_jobs=-1,
        ),
    }

    results = {}

    for model_name in models_debit:
        print(f"\n[Trainer] Training {model_name} for DEBIT account...")
        md = models_debit[model_name]
        md.fit(X_train, yd_train)
        metrics_debit = evaluate_model(md, X_test, yd_test, f"{model_name}/debit")

        print(f"[Trainer] Training {model_name} for CREDIT account...")
        mc = models_credit[model_name]
        mc.fit(X_train, yc_train)
        metrics_credit = evaluate_model(mc, X_test, yc_test, f"{model_name}/credit")

        results[model_name] = {
            "debit": metrics_debit,
            "credit": metrics_credit,
            "avg_f1": (metrics_debit["f1"] + metrics_credit["f1"]) / 2,
        }

        joblib.dump(md, MODELS_DIR / f"{model_name}_debit.pkl")
        joblib.dump(mc, MODELS_DIR / f"{model_name}_credit.pkl")

    best_model_name = max(results, key=lambda k: results[k]["avg_f1"])
    print(f"\n[Trainer] Best model: {best_model_name} (avg F1={results[best_model_name]['avg_f1']:.3f})")

    fe.save(MODELS_DIR)

    meta = {
        "best_model": best_model_name,
        "training_samples": len(df),
        "feature_count": X.shape[1],
        "debit_classes": int(len(np.unique(y_debit))),
        "credit_classes": int(len(np.unique(y_credit))),
        "results": {k: {
            "debit": {m: float(v) for m, v in r["debit"].items()},
            "credit": {m: float(v) for m, v in r["credit"].items()},
            "avg_f1": float(r["avg_f1"]),
        } for k, r in results.items()},
        "debit_classes_list": fe.label_encoder_debit.classes_.tolist(),
        "credit_classes_list": fe.label_encoder_credit.classes_.tolist(),
    }

    with open(MODELS_DIR / "model_meta.json", "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)

    print(f"\n[Trainer] Models saved to {MODELS_DIR}")
    print(f"[Trainer] Training complete!")
    return meta


if __name__ == "__main__":
    train_models()
