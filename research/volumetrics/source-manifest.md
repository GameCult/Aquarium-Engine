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

## Not Fully Cached

- Bruneton & Neyret 2008 original paper: direct PDF downloads from KlayGE and
  CiteSeer failed from this environment. Stable references:
  - https://www.klayge.org/material/4_0/Atmospheric/Precomputed%20Atmospheric%20Scattering.pdf
  - https://citeseerx.ist.psu.edu/document?doi=5cd50191a346535eef7e12c024592c3afdd29ad4&repid=rep1&type=pdf
  - DOI: https://doi.org/10.1111/j.1467-8659.2008.01245.x
- A separate Dreams talk transcript was not found. The compressed SIGGRAPH PDF
  contains substantial presenter-note text and is the preserved transcript-like
  source for this pass.
