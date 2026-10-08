"""Prepare bounded, cached Workshop previews as static Panorama addon resources.

No server files are modified; generated CSS contains only local compiled resources.
Install tools/veto-preview-requirements.txt before preparing images.
"""

import argparse
import hashlib
import io
import json
import math
import os
import re
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

ENDPOINT = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/"
CDN_HOSTS = ("steamusercontent.com", "steamuserimages-a.akamaihd.net", "steamcdn-a.akamaihd.net", "steamstatic.com")
MAX_MAPS = 128
MAX_METADATA_BYTES = 1_048_576
MAX_IMAGE_BYTES = 5_242_880
MAX_CACHE_BYTES = 64 * MAX_IMAGE_BYTES
CACHE_TTL = 86400
NEGATIVE_TTL = 300
NETWORK_BUDGET = 45
TEXTURE_ROOT = "panorama/styles/custom_game/anocore/previews"


def read_json(path):
    if path.stat().st_size > MAX_METADATA_BYTES:
        raise ValueError("JSON input exceeds 1 MiB")
    return json.loads(path.read_text(encoding="utf-8-sig"))


def valid_preview_url(value):
    if not isinstance(value, str) or not value or len(value) > 2048 or any(ord(c) < 33 or ord(c) > 126 for c in value):
        return False
    try:
        url = urllib.parse.urlsplit(value)
        return (url.scheme == "https" and url.port in (None, 443) and not url.username and not url.password
                and not url.fragment and "\\" not in value
                and any(url.hostname == host or url.hostname.endswith("." + host) for host in CDN_HOSTS))
    except (ValueError, AttributeError):
        return False


class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        if not valid_preview_url(newurl):
            raise ValueError("Preview redirect is outside the permitted Steam CDN hosts")
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def request_bytes(url, limit, deadline, data=None):
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise TimeoutError("Preview preparation network budget exhausted")
    if url != ENDPOINT and not valid_preview_url(url):
        raise ValueError("Invalid preview URL")
    request = urllib.request.Request(url, data=data, headers={"User-Agent": "AnoCore-Preview-Preparer/1"})
    with urllib.request.build_opener(SafeRedirect()).open(request, timeout=min(5, remaining)) as response:
        chunks, size = [], 0
        while True:
            if time.monotonic() >= deadline:
                raise TimeoutError("Preview preparation network budget exhausted")
            chunk = response.read(min(65536, limit + 1 - size))
            if not chunk:
                return b"".join(chunks)
            chunks.append(chunk)
            size += len(chunk)
            if size > limit:
                raise ValueError("Steam response exceeds the configured size limit")


def canonical(data):
    if not isinstance(data, dict):
        raise ValueError("JSON object required")
    result = {}
    for key, value in data.items():
        name = key.lower()
        if name in result:
            raise ValueError("Duplicate case-insensitive property")
        result[name] = value
    return result


def validate_maps(data):
    maps = canonical(data).get("maps")
    if not isinstance(maps, list) or not 1 <= len(maps) <= MAX_MAPS:
        raise ValueError("Maps must contain 1..128 configured maps")
    names, ids, workshops, result = set(), set(), set(), []
    for value in maps:
        item = canonical(value)
        name, map_id, workshop = item.get("displayname"), item.get("mapid"), item.get("workshopid")
        if (not isinstance(name, str) or not name.strip() or not isinstance(map_id, str)
                or not re.fullmatch(r"[a-zA-Z0-9_./-]{1,128}", map_id.strip())
                or ".." in map_id.split("/") or map_id.startswith("/")):
            raise ValueError("Map requires a display name and safe map ID")
        name, map_id = name.strip(), map_id.strip()
        if name.casefold() in names or map_id.casefold() in ids:
            raise ValueError("Duplicate map name or map ID")
        if workshop is not None and (type(workshop) is not int or not 1 <= workshop <= 2**64 - 1 or workshop in workshops):
            raise ValueError("Workshop IDs must be unique positive uint64 integers")
        names.add(name.casefold())
        ids.add(map_id.casefold())
        if workshop is not None:
            workshops.add(workshop)
        result.append({"DisplayName": name, "MapId": map_id, "WorkshopId": workshop})
    return result


def preview_class(map_definition):
    workshop = map_definition.get("WorkshopId")
    return (f"ano_preview_w_{workshop}" if workshop is not None else
            "ano_preview_m_" + hashlib.sha256(map_definition["MapId"].encode("utf-8")).hexdigest()[:16])


def validate_metadata(value, workshop):
    if not isinstance(value, dict):
        return None
    if (str(value.get("publishedfileid")) != str(workshop) or value.get("result") != 1
            or value.get("consumer_app_id") != 730 or not valid_preview_url(value.get("preview_url"))):
        return None
    title = value.get("title")
    if not isinstance(title, str) or not title.strip() or len(title) > 256 or any(ord(c) < 32 for c in title):
        return None
    return {"publishedfileid": str(workshop), "result": 1, "consumer_app_id": 730,
            "title": title, "preview_url": value["preview_url"]}


def atomic_json(path, data):
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent, delete=False) as stream:
        temporary = Path(stream.name)
        try:
            stream.write(json.dumps(data, indent=2) + "\n")
            stream.close()
            temporary.replace(path)
        finally:
            temporary.unlink(missing_ok=True)


def load_metadata(ids, cache, offline, deadline):
    result, pending = {}, []
    for workshop in ids:
        try:
            stored = read_json(cache / f"{workshop}.json")
            checked = stored["checked_at"]
            metadata = validate_metadata(stored.get("metadata"), workshop)
            ttl = CACHE_TTL if metadata else NEGATIVE_TTL
            fresh = (isinstance(checked, (int, float)) and math.isfinite(checked)
                     and 0 <= time.time() - checked < ttl)
            if offline or fresh:
                result[workshop] = metadata
                continue
        except (OSError, ValueError, KeyError, TypeError):
            pass
        if offline:
            result[workshop] = None
        else:
            pending.append(workshop)
    for start in range(0, len(pending), 50):
        batch = pending[start:start + 50]
        data = {"itemcount": str(len(batch))} | {f"publishedfileids[{i}]": str(w) for i, w in enumerate(batch)}
        details = {}
        try:
            body = request_bytes(ENDPOINT, MAX_METADATA_BYTES, deadline, urllib.parse.urlencode(data).encode("ascii"))
            response = json.loads(body)["response"]["publishedfiledetails"]
            if not isinstance(response, list) or len(response) > 50:
                raise ValueError("Unexpected metadata batch")
            for item in response:
                if isinstance(item, dict) and str(item.get("publishedfileid")).isdigit():
                    workshop = int(item["publishedfileid"])
                    if workshop in batch:
                        details[workshop] = validate_metadata(item, workshop)
        except (OSError, ValueError, KeyError, TypeError, TimeoutError):
            pass
        for workshop in batch:
            result[workshop] = details.get(workshop)
            atomic_json(cache / f"{workshop}.json", {"checked_at": time.time(), "metadata": result[workshop]})
    return result


def normalize_image(raw):
    from PIL import Image, ImageOps
    if len(raw) > MAX_IMAGE_BYTES:
        raise ValueError("Image exceeds 5 MiB")
    try:
        with Image.open(io.BytesIO(raw)) as source:
            if source.format not in ("PNG", "JPEG", "WEBP") or source.width * source.height > 16_000_000:
                raise ValueError("Unsupported or oversized image")
            image = ImageOps.exif_transpose(source).convert("RGB")
            image.thumbnail((512, 288))
            output = io.BytesIO()
            image.save(output, format="PNG")
            return output.getvalue()
    except Image.DecompressionBombError as error:
        raise ValueError("Oversized image") from error


def prune_cache(cache):
    # Only dedicated preparer cache files, never arbitrary directories/server configs.
    files = [p for p in cache.iterdir() if p.is_file() and re.fullmatch(r"([0-9]+|[a-f0-9]{64})\.(json|png)", p.name)]
    size = sum(p.stat().st_size for p in files)
    for index, path in enumerate(sorted(files, key=lambda p: p.stat().st_mtime)):
        if size <= MAX_CACHE_BYTES and len(files) - index <= MAX_MAPS * 2:
            break
        size -= path.stat().st_size
        path.unlink()


def prepare(maps_path, output, cache, offline=False, standard_images=None):
    output, cache = Path(output), Path(cache)
    if output.exists():
        raise ValueError("Output already exists; choose a new directory")
    if output.resolve().is_relative_to(cache.resolve()) or cache.resolve().is_relative_to(output.resolve()):
        raise ValueError("Output and cache directories must be separate")
    maps = validate_maps(read_json(Path(maps_path)))
    images = read_json(Path(standard_images)) if standard_images else {}
    if (not isinstance(images, dict)
            or any(key not in {m["MapId"] for m in maps if m["WorkshopId"] is None} for key in images)
            or any(not isinstance(value, str) or not value.strip() for value in images.values())):
        raise ValueError("Standard images must map configured non-Workshop map IDs to local files")
    # Fail clearly for a missing dependency before any output/cache is changed.
    from PIL import Image  # noqa: F401
    cache.mkdir(parents=True, exist_ok=True)
    prune_cache(cache)
    deadline = time.monotonic() + NETWORK_BUDGET
    metadata = load_metadata([m["WorkshopId"] for m in maps if m["WorkshopId"]], cache, offline, deadline)
    template = (Path(__file__).resolve().parents[1] / "ui/AnoCore/styles/custom_game/anocore/anomeme_banner.vtex").read_text()
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="ano-veto-", dir=output.parent) as temporary:
        staging = Path(temporary)
        (staging / "previews").mkdir()
        manifest, rules = [], ["/* Generated static map previews; identity is preserved. */"]
        for item in maps:
            workshop = item["WorkshopId"]
            details = metadata.get(workshop)
            key = preview_class(item)
            png = None
            if workshop is None and item["MapId"] in images:
                local = Path(standard_images).resolve().parent / images[item["MapId"]]
                if local.stat().st_size > MAX_IMAGE_BYTES:
                    raise ValueError("Standard map image exceeds 5 MiB")
                png = normalize_image(local.read_bytes())
            elif details:
                image_cache = cache / (hashlib.sha256(details["preview_url"].encode("utf-8")).hexdigest() + ".png")
                try:
                    if image_cache.exists():
                        if image_cache.stat().st_size > MAX_IMAGE_BYTES:
                            raise ValueError("Cached image exceeds 5 MiB")
                        png = normalize_image(image_cache.read_bytes())
                    elif not offline:
                        png = normalize_image(request_bytes(details["preview_url"], MAX_IMAGE_BYTES, deadline))
                        image_cache.write_bytes(png)
                except (OSError, ValueError, TimeoutError):
                    png = None
            if png:
                (staging / "previews" / f"{key}.png").write_bytes(png)
                descriptor = template.replace("panorama/styles/custom_game/anocore/anomeme_banner.png", f"{TEXTURE_ROOT}/{key}.png")
                (staging / "previews" / f"{key}.vtex").write_text(descriptor, encoding="utf-8")
                rules.append(f'.{key} {{ background-image: url("s2r://{TEXTURE_ROOT}/{key}.vtex"); }}')
            manifest.append(item | {"PreviewClass": key, "WorkshopTitle": details["title"] if details else None,
                                    "PreviewUrl": details["preview_url"] if details else None, "HasImage": bool(png)})
        (staging / "ano_veto_previews.css").write_text("\n".join(rules) + "\n", encoding="utf-8")
        atomic_json(staging / "manifest.json", {"Maps": manifest})
        # Atomic directory publication; never partially replace an existing build.
        os.rename(staging, output)
    prune_cache(cache)
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--maps", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--cache", required=True, type=Path, help="Dedicated preparer cache directory")
    parser.add_argument("--standard-images", type=Path, help="Map ID to local image filename JSON (non-Workshop maps only)")
    parser.add_argument("--offline", action="store_true")
    args = parser.parse_args()
    try:
        result = prepare(args.maps, args.output, args.cache, args.offline, args.standard_images)
    except (OSError, ValueError, ImportError) as error:
        parser.exit(1, f"Preview preparation failed: {error}\n")
    print(f"Prepared {sum(m['HasImage'] for m in result)}/{len(result)} map images. Missing images use a neutral card.")


if __name__ == "__main__":
    main()
