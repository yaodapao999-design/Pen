# Slick Oil Texture Sources

These textures support the Slick Oil Core liquid trail effect.

## Imported CC0 Source

- Source: OpenGameArt, "Seamless water tiles - light_water_0.jpg"
- URL: https://opengameart.org/content/seamless-water-tiles-lightwater0jpg
- Author: Hazmat Harry
- License: CC0
- Original filename: light_water_0.jpg
- Project filename: T_SlickOil_WaterDetail_Light.jpg

- Source: OpenGameArt, "Seamless water tiles - dark_water_0.jpg"
- URL: https://opengameart.org/node/174588
- Author: Hazmat Harry
- License: CC0
- Original filename: dark_water_0.jpg
- Project filename: T_SlickOil_WaterDetail_Dark.jpg

## Generated Project Asset

- T_SlickOil_WaterNormal.png
- Generated from T_SlickOil_WaterDetail_Light.jpg inside the Unity project.
- Used as the scrolling normal detail for the Slick Oil water-style shader.

- T_SlickOil_FoamNoise.png
- Procedurally generated in the Unity project.
- Used as the foam and edge-breakup texture for the Slick Oil splat shaders.

## Runtime Generated

- T_SlickOil_SplatMap_Runtime
- Generated in memory by SlickOilSplatMap while the oil core paints the tabletop.
- Mirrors the Splatoon-style tutorial workflow: gameplay writes a surface-space splat mask, and the shader reads that mask to blend liquid color, edge lift, foam breakup, animated normals, and glossy highlights.
