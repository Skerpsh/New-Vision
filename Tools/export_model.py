# Copyright (c) 2026 New Vision contributors
# SPDX-License-Identifier: AGPL-3.0-only

"""Exports an Ultralytics YOLO model to ONNX for New Vision and puts it into the Unity project.

Usage (from any folder, with the venv that has `ultralytics` installed):
    python Tools/export_model.py yolo11n 416
    python Tools/export_model.py yolo11m 320
    python Tools/export_model.py path/to/my_trained.pt 416 my_model_416

Open-vocabulary YOLO-World models detect whatever the labels file lists (no training):
    python Tools/export_model.py yolov8s-worldv2 416 yoloworld_s_416

Then in Unity: New Vision > Convert Models. The model shows up in the in-app menu ("Model" row).
The model's classes must be the first lines, in the same order, of
Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Model/SentisYoloClasses.txt
(COCO models use the first 80; extra classes for YOLO-World are added at the end).
"""
import argparse
import shutil
from pathlib import Path

from ultralytics import YOLO, YOLOWorld

MODEL_DIR = Path(__file__).resolve().parent.parent / "Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Model"
LABELS = MODEL_DIR / "SentisYoloClasses.txt"

# Words YOLO-World searches for, when the label shown in the app doesn't work as a prompt.
# Tested on headset frames: the model doesn't know "vape" and sees a pod vape as a remote control.
PROMPTS = {"Vape": "remote control"}


def replace_einsum(onnx_path):
    """Rewrites the Einsum nodes of YOLO-World as Reshape/Transpose/MatMul.

    Unity Inference Engine imports Einsum, but on the headset the model then finds nothing right
    (the same ONNX works on the PC), so the model is given only ops that plain YOLO uses too.
    """
    import onnx
    from onnx import helper, numpy_helper, shape_inference
    import numpy as np

    model = shape_inference.infer_shapes(onnx.load(onnx_path))
    graph = model.graph
    shapes = {v.name: [d.dim_value for d in v.type.tensor_type.shape.dim] for v in list(graph.value_info) + list(graph.input)}
    # The class embeddings (second Einsum input) are baked in as constants
    shapes.update({i.name: list(i.dims) for i in graph.initializer})
    nodes = []
    replaced = 0

    def const_shape(name, dims):
        graph.initializer.append(numpy_helper.from_array(np.array(dims, dtype=np.int64), name))
        return name

    for node in graph.node:
        if node.op_type != "Einsum":
            nodes.append(node)
            continue
        equation = helper.get_attribute_value(node.attribute[0]).decode()
        x, y = node.input
        out = node.output[0]
        p = f"{node.name or out}_mm"
        if equation == "bchw,bkc->bkhw":
            # (b,c,h,w) -> (b,c,hw); (b,k,c) @ (b,c,hw) -> (b,k,hw) -> (b,k,h,w)
            b, c, h, w = shapes[x]
            k = shapes[y][1]
            nodes += [
                helper.make_node("Reshape", [x, const_shape(p + "_s1", [b, c, h * w])], [p + "_x"]),
                helper.make_node("MatMul", [y, p + "_x"], [p + "_m"]),
                helper.make_node("Reshape", [p + "_m", const_shape(p + "_s2", [b, k, h, w])], [out]),
            ]
        elif equation == "bmchw,bnmc->bmhwn":
            # (b,m,c,h,w) -> (b,m,hw,c); (b,n,m,c) -> (b,m,c,n); matmul -> (b,m,hw,n) -> (b,m,h,w,n)
            b, m, c, h, w = shapes[x]
            n = shapes[y][1]
            nodes += [
                helper.make_node("Reshape", [x, const_shape(p + "_s1", [b, m, c, h * w])], [p + "_x"]),
                helper.make_node("Transpose", [p + "_x"], [p + "_xt"], perm=[0, 1, 3, 2]),
                helper.make_node("Transpose", [y], [p + "_yt"], perm=[0, 2, 3, 1]),
                helper.make_node("MatMul", [p + "_xt", p + "_yt"], [p + "_m"]),
                helper.make_node("Reshape", [p + "_m", const_shape(p + "_s2", [b, m, h, w, n])], [out]),
            ]
        else:
            raise ValueError(f"Unknown Einsum equation {equation}")
        replaced += 1

    del graph.node[:]
    graph.node.extend(nodes)
    onnx.checker.check_model(model)
    onnx.save(model, onnx_path)
    print(f"Replaced {replaced} Einsum nodes with MatMul")


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

    if "world" in model_name:
        labels = [line.strip() for line in LABELS.read_text().splitlines() if line.strip()]
        model = YOLOWorld(model_name)
        model.set_classes([PROMPTS.get(label, label) for label in labels])
        print(f"YOLO-World classes: {len(labels)} from {LABELS.name}")
    else:
        model = YOLO(model_name)

    # opset 15 is supported by Unity Inference Engine; no NMS in the graph (it is done in Unity)
    onnx_path = Path(model.export(format="onnx", imgsz=args.size, opset=15, simplify=True, dynamic=False, nms=False))
    if "world" in model_name:
        replace_einsum(onnx_path)
    target = MODEL_DIR / f"{name}.onnx"
    shutil.copyfile(onnx_path, target)
    print(f"Copied to {target}\nNext: Unity > New Vision > Convert Models")


if __name__ == "__main__":
    main()
