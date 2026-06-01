from fastapi import FastAPI, File, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
import tensorflow as tf
import numpy as np
from PIL import Image
import io
import zipfile
import tempfile
import os
import base64
from tensorflow.keras.applications import ResNet50, MobileNetV2
from tensorflow.keras.applications.resnet50 import preprocess_input as resnet_preprocess
from tensorflow.keras.layers import Flatten, Dense, Dropout, Input, GlobalAveragePooling2D
from tensorflow.keras import Sequential

# 1. Initializing the app
app = FastAPI()

# 2. Allowing Blazor to talk to Python
app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"])

# 3. Helper to extract weights from .keras zip
def extract_and_load_weights(model, keras_path):
    with zipfile.ZipFile(keras_path, 'r') as z:
        with tempfile.TemporaryDirectory() as tmpdir:
            z.extractall(tmpdir)
            print(f"Files in zip: {os.listdir(tmpdir)}")
            weight_file = os.path.join(tmpdir, "model.weights.h5")
            if os.path.exists(weight_file):
                model.load_weights(weight_file, skip_mismatch=True)
            else:
                for f in os.listdir(tmpdir):
                    if f.endswith(".h5"):
                        model.load_weights(os.path.join(tmpdir, f), skip_mismatch=True)
                        break
    return model

# 4. Rebuilding ResNet50 exactly as trained
def load_resnet_model(keras_path):
    base = ResNet50(weights=None, include_top=False, input_shape=(224, 224, 3))
    base.trainable = False
    model = Sequential([
        Input(shape=(224, 224, 3)),
        base,
        Flatten(),
        Dense(256, activation='relu'),
        Dropout(0.5),
        Dense(7, activation='softmax')
    ], name="ResNet50")
    return extract_and_load_weights(model, keras_path)

# 5. Rebuilding MobileNetV2 exactly as trained
def load_mobilenet_model(keras_path):
    base = MobileNetV2(weights=None, include_top=False, input_shape=(224, 224, 3))
    base.trainable = False
    model = Sequential([
        Input(shape=(224, 224, 3)),
        base,
        GlobalAveragePooling2D(),
        Dense(256, activation='relu'),
        Dropout(0.5),
        Dense(38, activation='softmax')
    ], name="MobileNetV2")
    return extract_and_load_weights(model, keras_path)

print("Loading ResNet50 soil model...")
model_classify = load_resnet_model("ArviaApp_ResNet50_Model.keras")
print("ResNet50 loaded!")

print("Loading MobileNetV2 disease model...")
model_detect = load_mobilenet_model("PlantDisease_MobileNetV2_Model.keras")
print("MobileNetV2 loaded!")

# 6. Preprocessing functions
def preprocess_resnet(file_bytes: bytes):
    img = Image.open(io.BytesIO(file_bytes)).convert("RGB").resize((224, 224))
    arr = np.array(img, dtype=np.float32)
    arr = resnet_preprocess(arr)
    return np.expand_dims(arr, axis=0)

def preprocess_mobilenet(file_bytes: bytes):
    img = Image.open(io.BytesIO(file_bytes)).convert("RGB").resize((224, 224))
    arr = np.array(img, dtype=np.float32) / 255.0
    return np.expand_dims(arr, axis=0)

# 7. Shared prediction logic
def run_prediction(file_bytes: bytes):
    soil_preds = model_classify.predict(preprocess_resnet(file_bytes))
    soil_index = np.argmax(soil_preds)
    soil_classes = ["Alluvial_Soil", "Arid_Soil", "Black_Soil", "Laterite_Soil", "Mountain_Soil", "Red_Soil", "Yellow_Soil"]
    soil_result = soil_classes[soil_index]

    disease_preds = model_detect.predict(preprocess_mobilenet(file_bytes))
    disease_index = np.argmax(disease_preds)
    disease_classes = [
        "Apple___Apple_scab", "Apple___Black_rot", "Apple___Cedar_apple_rust", "Apple___healthy", "Blueberry___healthy",
        "Cherry_(including_sour)___Powdery_mildew", "Cherry_(including_sour)___healthy", "Corn_(maize)___Cercospora_leaf_spot Gray_leaf_spot",
        "Corn_(maize)___Common_rust_", "Corn_(maize)___Northern_Leaf_Blight", "Corn_(maize)___healthy", "Grape___Black_rot",
        "Grape___Esca_(Black_Measles)", "Grape___Leaf_blight_(Isariopsis_Leaf_Spot)", "Grape___healthy", "Orange___Haunglongbing_(Citrus_greening)",
        "Peach___Bacterial_spot", "Peach___healthy", "Pepper,_bell___Bacterial_spot", "Pepper,_bell___healthy", "Potato___Early_blight",
        "Potato___Late_blight", "Potato___healthy", "Raspberry___healthy", "Soybean___healthy", "Squash___Powdery_mildew",
        "Strawberry___Leaf_scorch", "Strawberry___healthy", "Tomato___Bacterial_spot", "Tomato___Early_blight", "Tomato___Late_blight",
        "Tomato___Leaf_Mold", "Tomato___Septoria_leaf_spot", "Tomato___Spider_mites Two-spotted_spider_mite", "Tomato___Target_Spot",
        "Tomato___Tomato_Yellow_Leaf_Curl_Virus", "Tomato___Tomato_mosaic_virus", "Tomato___healthy"
    ]
    disease_result = disease_classes[disease_index]

    return {"soilType": soil_result, "healthStatus": disease_result}

# 8. Endpoint for upload page (multipart file)
@app.post("/predict")
async def predict(file: UploadFile = File(...)):
    file_bytes = await file.read()
    return run_prediction(file_bytes)

# 9. Endpoint for live camera page (base64 JSON)
class ImagePayload(BaseModel):
    image: str

@app.post("/predict-base64")
async def predict_base64(payload: ImagePayload):
    image_data = payload.image
    if "," in image_data:
        image_data = image_data.split(",")[1]
    file_bytes = base64.b64decode(image_data)
    return run_prediction(file_bytes)