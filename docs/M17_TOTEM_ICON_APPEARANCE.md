# Native totem icon appearance

Duckov's `ItemDisplay.Setup` uses the ordinary item sprite with white image color, then applies a `LeTai.TrueShadow.TrueShadow` quality glow. `ItemMetaData.displayQuality` and `GameplayDataSettings.UIStyle.ApplyDisplayQualityShadow` expose the same native color, offset and inset mapping. UDS uses these installed contracts for totems in retained Equipment and Runs, including captured loadout grids and item detail. It does not colorize a generic image or infer a quality from a localized name.

Read-only inspection of Duckov 2.3.30 / Steam build 24013657 `resources.assets` found matching ItemDisplay quality-shadow settings on prefab pairs 77829/94943, 77862/95100, 78014/94723 and 78061/94653: size 3, spread 0.5, caster alpha enabled, caster color ignored, and external activation ignored. Colors, offset and inset remain native style inputs. Shadow rendering remains inside the icon hierarchy and inherits native mask handling. The native component owns its rendering resources and releases them when destroyed.

`NativeTotemIconAppearance` checks exact TypeID metadata, matching sprite and native Totem tag before applying the glow. Unknown/missing metadata and non-totem or fallback icons do not acquire a color. Pooled Equipment rows disable the glow when cleared or rebound to other content. The game-provided LeTai.TrueShadow assembly is a compile-time reference with `Private=false`; it is not redistributed. The mod package remains the usual five files.

The direct-totem accordion places total equipped duration beneath the item name, with compact `Totem slot N: duration` detail lines and a continuous translucent expanded background. Inactive or unknown activation remains explicitly qualified. Multiple activation intervals for one slot remain separately qualified; presentation does not merge identities or relabel presence as effect activity.

Native color, silhouette, clipping, scaling and visual similarity still require user-controlled screenshot verification. Automated compilation and presentation tests do not qualify Unity rendering or gameplay.
