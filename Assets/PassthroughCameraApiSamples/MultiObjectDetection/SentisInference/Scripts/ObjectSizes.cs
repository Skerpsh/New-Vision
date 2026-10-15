using System.Collections.Generic;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Typical real-world height (meters) of COCO objects as they usually stand. Used to
    /// 1) reject detections whose measured size is impossible for the class (e.g. a "car" 10 cm tall is a picture or a toy),
    /// 2) estimate the distance to objects beyond the depth sensor's range (a car 1.5 m tall that looks this big is that far away).
    /// Values are rough on purpose; the checks allow a wide tolerance around them.
    /// </summary>
    public static class ObjectSizes
    {
        private static readonly Dictionary<string, float> s_typicalHeight = new()
        {
            // People and vehicles
            { "Person", 1.7f }, { "Bicycle", 1.05f }, { "Car", 1.5f }, { "Motorcycle", 1.15f }, { "Airplane", 8f },
            { "Bus", 3.1f }, { "Train", 4f }, { "Truck", 3f }, { "Boat", 2f },
            // Street
            { "Traffic light", 1f }, { "Fire hydrant", 0.75f }, { "Stop sign", 0.75f }, { "Parking meter", 1.4f }, { "Bench", 0.85f },
            // Animals
            { "Bird", 0.25f }, { "Cat", 0.3f }, { "Dog", 0.55f }, { "Horse", 1.6f }, { "Sheep", 0.9f }, { "Cow", 1.4f },
            { "Elephant", 3f }, { "Bear", 1.2f }, { "Zebra", 1.4f }, { "Giraffe", 5f },
            // Accessories
            { "Backpack", 0.45f }, { "Umbrella", 0.9f }, { "Handbag", 0.3f }, { "Tie", 0.45f }, { "Suitcase", 0.6f },
            // Sports
            { "Frisbee", 0.25f }, { "Skis", 1.7f }, { "Snowboard", 1.5f }, { "Sports ball", 0.22f }, { "Kite", 0.8f },
            { "Baseball bat", 0.85f }, { "Baseball glove", 0.28f }, { "Skateboard", 0.3f }, { "Surfboard", 2f }, { "Tennis racket", 0.68f },
            // Kitchen
            { "Bottle", 0.27f }, { "Wine glass", 0.2f }, { "Cup", 0.11f }, { "Fork", 0.19f }, { "Knife", 0.22f }, { "Spoon", 0.17f },
            { "Bowl", 0.09f },
            // Food
            { "Banana", 0.18f }, { "Apple", 0.08f }, { "Sandwich", 0.1f }, { "Orange", 0.08f }, { "Broccoli", 0.15f }, { "Carrot", 0.18f },
            { "Hot dog", 0.08f }, { "Pizza", 0.3f }, { "Donut", 0.09f }, { "Cake", 0.15f },
            // Furniture
            { "Chair", 0.9f }, { "Couch", 0.85f }, { "Potted plant", 0.6f }, { "Bed", 0.8f }, { "Dining table", 0.75f }, { "Toilet", 0.75f },
            // Electronics
            { "TV", 0.65f }, { "Laptop", 0.25f }, { "Mouse", 0.06f }, { "Remote", 0.18f }, { "Keyboard", 0.15f }, { "Cell phone", 0.15f },
            // Appliances
            { "Microwave", 0.32f }, { "Oven", 0.85f }, { "Toaster", 0.2f }, { "Sink", 0.4f }, { "Refrigerator", 1.8f },
            // Indoor
            { "Book", 0.24f }, { "Clock", 0.3f }, { "Vase", 0.3f }, { "Scissors", 0.2f }, { "Teddy bear", 0.35f },
            { "Hair drier", 0.25f }, { "Toothbrush", 0.19f },
        };

        /// <summary>Typical height in meters, or null if unknown.</summary>
        public static float? TypicalHeight(string className) =>
            s_typicalHeight.TryGetValue(className, out var height) ? height : null;
    }
}
