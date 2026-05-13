from dataclasses import dataclass
from typing import Optional


@dataclass
class FileFeature:
    name: str               # "AnimeRecomList.PNG"
    full_path: str          # "H:\\SDMSTest\\Anime Lists\\AnimeRecomList.PNG"
    extension: str          # ".PNG"
    mime_type: str          # "image"
    size_bytes: int
    depth: int              # 1
    parent_chain: list[str] # ["Anime Lists"]
    sibling_count: int
    folder_name: str        # "Anime Lists"

    cluster_id: Optional[int] = None  # set by llm_clusterer