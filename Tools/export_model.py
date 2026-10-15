"""Exports an Ultralytics YOLO model to ONNX for New Vision and puts it into the Unity project.

Usage (from any folder, with the venv that has `ultralytics` installed):
    python Tools/export_model.py yolo11n 416
    python Tools/export_model.py yolo11m 320
    python Tools/export_model.py path/to/my_trained.pt 416 my_model_416

Then in Unity: New Vision > Convert Models. The model shows up in the in-app menu ("Model" row).
The model must detect the same classes, in the same order, as
Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Model/SentisYoloClasses.txt
"""
import argparse
import shutil
from pathlib import Path

from ultralytics import YOLO

MODEL_DIR = Path(__file__).resolve().parent.parent / "Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Model"


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("model", help="model name (e.g. yolo11n, downloaded automatically) or path to a .pt file")
    parser.add_argument("size", type=int, help="square input size, multiple of 32 (e.g. 320, 416, 480)")
    parser.add_argument("name", nargs="?", help="file name in the project, without extension (default: <model>_<size>)")
    args = parser.parse_args()

    if args.size % 32:
        parser.error("size must be a multiple of 32")
    model_name = args.model if args.model.endswith(".pt") else args.model + ".pt"
    name = args.name or f"{Path(model_name).stem}_{args.size}"

    # opset 15 is supported by Unity Inference Engine; no NMS in the graph (it is done in Unity)
    onnx_path = Path(YOLO(model_name).export(format="onnx", imgsz=args.size, opset=15, simplify=True, dynamic=False, nms=False))
    target = MODEL_DIR / f"{name}.onnx"
    shutil.copyfile(onnx_path, target)
    print(f"Copied to {target}\nNext: Unity > New Vision > Convert Models")


if __name__ == "__main__":
    main()
