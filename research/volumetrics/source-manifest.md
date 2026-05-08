# Volumetrics Source Manifest

Local binary/text sources are cached under `research/volumetrics/sources/` and
working text extracts under `research/volumetrics/extracted-text/`. These folders
are gitignored; rerun the listed URLs if the cache is missing.

## Cached Sources

| Topic | Local file | URL | Notes |
| --- | --- | --- | --- |
| Bruneton atmosphere implementation | `bruneton-2017-implementation.html` | https://ebruneton.github.io/precomputed_atmospheric_scattering/ | 2017 documented implementation of Bruneton/Neyret precomputed atmospheric scattering. |
| Bruneton atmosphere shader docs | `bruneton-functions.glsl.html` | https://ebruneton.github.io/precomputed_atmospheric_scattering/atmosphere/functions.glsl.html | Core transmittance, single scattering, multiple scattering, irradiance, and lookup functions. |
| Wronski volumetric fog | `wronski-volumetric-fog-siggraph2014-advances.pdf` | https://www.realtimerendering.com/advances/s2014/wronski/bwronski_volumetric_fog_siggraph2014.pdf | SIGGRAPH 2014 Assassin's Creed IV volumetric fog deck with presenter notes. |
| Frostbite unified volumetrics | `hillaire-frostbite-pb-unified-volumetrics-siggraph2015.pptx` | https://advances.realtimerendering.com/s2015/Frostbite%20PB%20and%20unified%20volumetrics.pptx | Hillaire/Frostbite SIGGRAPH 2015 deck; large binary, local only. |
| Frostbite sky/atmosphere/clouds | `hillaire-frostbite-sky-atmosphere-clouds-siggraph2016.pdf` | https://media.contentapi.ea.com/content/dam/eacom/frostbite/files/s2016-pbs-frostbite-sky-clouds-new.pdf | SIGGRAPH 2016 physically based sky, atmosphere, and cloud rendering notes. |
| Dreams Learning from Failure | `dreams-learning-from-failure-siggraph2015-compressed.pdf` | https://advances.realtimerendering.com/s2015/AlexEvans_SIGGRAPH-2015-sml.pdf | Compressed SIGGRAPH 2015 Media Molecule deck with transcript-like presenter notes. |
| Dreams Media Molecule post | `dreams-learning-from-failure-mediamolecule.html` | https://www.mediamolecule.com/blog/article/siggraph_2015 | Official post for Alex Evans's SIGGRAPH talk. |
| Dreams Umbra video page | `dreams-learning-from-failure-video-page.html` | https://www.mediamolecule.com/blog/article/alex_at_umbra_ignite_2015_learning_from_failure_video | Official video-page wrapper. I did not find a separate clean transcript. |
| GigaVoxels paper | `gigavoxels-ray-guided-streaming.pdf` | https://maverick.inria.fr/Publications/2009/CNLE09/CNLE09.pdf | I3D 2009 paper on ray-guided streaming and brick/mipmap volume rendering. |
| GigaVoxels publication page | `gigavoxels-publication-page.html` | https://www.icare3d.org/research-cat/publications/gigavoxels-ray-guided-streaming-for-efficient-and-detailed-voxel-rendering.html | Abstract, authors, and publication metadata. |
| Horizon volumetric clouds | `schneider-horizon-volumetric-cloudscapes-siggraph2015.pdf` | https://advances.realtimerendering.com/s2015/The%20Real-time%20Volumetric%20Cloudscapes%20of%20Horizon%20-%20Zero%20Dawn%20-%20ARTR.pdf | Guerrilla SIGGRAPH 2015 deck on cloud modeling, lighting, and optimization. |
| Stable volume sampling | `bowles-fast-stable-volume-rendering-siggraph2015.pptx` | https://advances.realtimerendering.com/s2015/siggraph15_volsampling.pptx | Studio Gobo talk on stable undersampled volume rendering. |
| NVIDIA volumetric light scattering | `nvidia-fast-flexible-volumetric-light-scattering-gdc2016.pdf` | https://developer.nvidia.com/sites/default/files/akamai/gameworks/downloads/papers/NVVL/Fast_Flexible_Physically-Based_Volumetric_Light_Scattering.pdf | GDC 2016/GameWorks-style physically based volumetric light scattering. |
| Unreal volumetric fog docs | `unreal-volumetric-fog-docs.html` | https://dev.epicgames.com/documentation/en-us/unreal-engine/volumetric-fog-in-unreal-engine?application_version=5.6 | Current engine docs on frustum volume texture, temporal reprojection, and tradeoffs. |
| Frostbite 2016 source page | `frostbite-sky-atmosphere-cloud-rendering.html` | https://www.frostbite.com/frostbite/news/physically-based-sky-atmosphere-and-cloud-rendering | Official page with presentation/course-note links. |
| Hillaire production sky/atmosphere | `hillaire-scalable-production-sky-atmosphere-2020.pdf` | https://diglib.eg.org/bitstream/handle/10.1111/cgf14050/v39i4pp013-022.pdf | EGSR 2020 paper; local fetch currently contains HTML despite the `.pdf` name, so use the URL if the cache is stale. |
| Hillaire sky/atmosphere source | `epic-sky-atmosphere-source-github.html` | https://github.com/sebh/UnrealEngineSkyAtmosphere | Companion source for the 2020 sky/atmosphere paper. |
| Advances 2022 index | `s2022-index.html.html` | https://advances.realtimerendering.com/s2022/index.html | Cached to preserve material links and abstracts. |
| Nubis Evolved | `nubis-evolved-volumetric-clouds-siggraph2022.pdf` | https://advances.realtimerendering.com/s2022/SIGGRAPH2022-Advances-NubisEvolved-NoVideos.pdf | SIGGRAPH Advances 2022 cloud deck without videos. |
| Advances 2023 index | `s2023-index.html.html` | https://advances.realtimerendering.com/s2023/index.html | Cached to preserve material links and abstracts. |
| Nubis Cubed page | `nubis-cubed-volumetric-clouds-siggraph2023-page.html` | https://www.guerrilla-games.com/read/nubis-cubed | Official Guerrilla page for the Nubis Cubed talk. |
| Nubis Cubed deck | `nubis-cubed-volumetric-clouds-siggraph2023.pdf` | https://advances.realtimerendering.com/s2023/Nubis%20Cubed%20(Advances%202023).pdf | SIGGRAPH Advances 2023 voxel-cloud deck; source for compressed SDF and adaptive ray-march notes. |
| Advances 2024 index | `s2024-index.html.html` | https://advances.realtimerendering.com/s2024/index.html | Cached to preserve material links and abstracts. |
| Neural Light Grid deck | `neural-light-grid-advances-2024.pdf` | https://advances.realtimerendering.com/s2024/content/Iwanicki/Advances_SIGGRAPH_2024_Neural_LightGrid.pdf | SIGGRAPH Advances 2024 slides and notes. |
| Neural Light Grid page | `neural-light-grid-activision-2024.html` | https://research.activision.com/publications/2024/08/Neural_Light_Grid | Activision research page for the technical memo and slides. |
| Epic dynamic occlusion with SDF | `wright-dynamic-occlusion-sdf-siggraph2015.pdf` | https://advances.realtimerendering.com/s2015/DynamicOcclusionWithSignedDistanceFields.pdf | Production deck on SDF cone tracing and object/global distance fields. |
| AMD sparse distance fields | `amd-real-time-sparse-distance-fields-for-games-2023.pdf` | https://gpuopen.com/download/GDC-2023-Sparse-Distance-Fields-For-Games.pdf | GDC 2023 GPUOpen/Brixelizer deck. |
| RTSDF soft shadows paper | `rtsdf-soft-shadow-approximation-games-2022.pdf` | https://www.scitepress.org/PublishedPapers/2022/109962/109962.pdf | Paper on realtime SDF generation for soft shadow approximation. |
| RTSDF arXiv version | `rtsdf-generating-sdf-realtime-soft-shadows-2022.pdf` | https://arxiv.org/pdf/2210.04449 | arXiv copy of the realtime SDF soft-shadow work. |
| nvblox GPU SDF mapping | `nvblox-gpu-incremental-sdf-mapping-2023.pdf` | https://arxiv.org/pdf/2311.00626 | Robotics-oriented but useful GPU TSDF/ESDF incremental mapping reference. |
| Cone-traced SDF supersampling | `cone-traced-supersampling-sdf-rendering-2023.pdf` | https://openreview.net/pdf?id=FYhiH9IyBq | SDF rendering/supersampling reference for cone-traced surface evaluation. |
| Adaptive multi-view radiance caching | `adaptive-multiview-radiance-caching-participating-media-2025.html` | https://diglib.eg.org/items/4a430a51-7cf1-428c-83c3-a93f2c0f1656 | Eurographics/CGF 2025 page for heterogeneous participating-media radiance caching. |
| Adaptive multi-view radiance caching PDF attempt | `adaptive-multiview-radiance-caching-participating-media-2025.pdf` | https://diglib.eg.org/bitstream/handle/10.1111/cgf70051/cgf70051.pdf | Local fetch currently contains HTML, not a valid PDF; indexed snippets were used for high-level notes. |
| Real-time underwater spectral rendering page | `realtime-underwater-spectral-rendering-2024.html` | https://diglib.eg.org/items/85d7d151-2738-4f99-9378-dc41a57e2c65 | Eurographics 2024 page for domain-specific participating-media rendering. |
| Real-time underwater spectral rendering PDF | `real-time-underwater-spectral-rendering-2024.pdf` | http://zaguan.unizar.es/record/135350/files/texto_completo.pdf?download=1 | Repository PDF mirror used after Wiley blocked automated download. |
| Ghost of Tsushima rendering page | `tsushima-real-time-samurai-cinema-2021.html` | https://advances.realtimerendering.com/s2021/jpatry_advances2021/index.html | Cached for atmosphere/tonemapping context; not central to the current volumetric plan. |

## Not Fully Cached

- Bruneton & Neyret 2008 original paper: direct PDF downloads from KlayGE and
  CiteSeer failed from this environment. Stable references:
  - https://www.klayge.org/material/4_0/Atmospheric/Precomputed%20Atmospheric%20Scattering.pdf
  - https://citeseerx.ist.psu.edu/document?doi=5cd50191a346535eef7e12c024592c3afdd29ad4&repid=rep1&type=pdf
  - DOI: https://doi.org/10.1111/j.1467-8659.2008.01245.x
- A separate Dreams talk transcript was not found. The compressed SIGGRAPH PDF
  contains substantial presenter-note text and is the preserved transcript-like
  source for this pass.
- The citation trail scan was pragmatic rather than exhaustive. Semantic Scholar
  API requests were rate-limited from this environment, so the durable follow-up
  notes rely on Advances pages, official project pages, direct papers/decks, and
  indexed paper snippets where direct PDF fetches failed.
