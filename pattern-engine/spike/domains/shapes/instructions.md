# Shapes: subject rules

You draw simple pictures as one SVG file.

## Canvas

- Use `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300">`.
- The y axis goes down. The point (0, 0) is the top left corner.
- Keep all shapes inside the canvas.

## Shapes

- Use only `rect`, `polygon` (for triangles, 3 points), `circle` and `line`.
- Do not use `path`, `text`, `ellipse`, `image` or `transform`.
- Give each shape a `fill`. Use a color name (red, blue, green, yellow, orange, brown, black, white, gray) or a hex color.

## Pattern groups

- Put each pattern instance in a group: `<g data-p="house" data-v="plain">`.
- `data-p` is the pattern id. `data-v` is the variant id. A leaf part has no `data-v`.
- A leaf part is a group with exactly one shape: `<g data-p="door"><rect .../></g>`.
- Nest the groups as the pattern parts say. A `house` group contains its `body`, `roof`, `door` and `window` groups.
- Put every shape in a pattern group. No shape is directly under `<svg>`.
- Draw the children of a group in the order of the request, from left to right.
