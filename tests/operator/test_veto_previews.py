import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("previews", Path(__file__).resolve().parents[2] / "tools/prepare_veto_previews.py")
previews = importlib.util.module_from_spec(spec)
spec.loader.exec_module(previews)


class VetoPreviewTests(unittest.TestCase):
    def test_identity_matches_native_and_preserves_case(self):
        self.assertEqual("ano_preview_w_123", previews.preview_class({"MapId": "custom", "WorkshopId": 123}))
        self.assertEqual("ano_preview_m_f99f33e2882aa01d", previews.preview_class({"MapId": "de_dust2"}))
        self.assertNotEqual(previews.preview_class({"MapId": "de_Dust2"}), previews.preview_class({"MapId": "de_dust2"}))

    def test_urls_reject_non_steam_injection_credentials_and_redirects(self):
        for url in ["http://steamusercontent.com/a", "https://localhost/a", "https://steamusercontent.com.evil.test/a",
                    "https://x@steamusercontent.com/a", "https://steamusercontent.com:444/a", "https://steamusercontent.com/a\n.css",
                    "https://steamusercontent.com/a#fragment"]:
            self.assertFalse(previews.valid_preview_url(url), url)
        self.assertTrue(previews.valid_preview_url("https://images.steamusercontent.com/ugc/123/a/?size=512"))
        with self.assertRaises(ValueError):
            previews.SafeRedirect().redirect_request(None, None, 302, "Found", {}, "https://localhost/private")

    def test_metadata_requires_correct_id_app_and_success(self):
        base = {"publishedfileid": "123", "result": 1, "consumer_app_id": 730,
                "title": "Actual Workshop Map", "preview_url": "https://images.steamusercontent.com/ugc/1/a"}
        self.assertEqual("Actual Workshop Map", previews.validate_metadata(base, 123)["title"])
        for update in [{"publishedfileid": "456"}, {"consumer_app_id": 440}, {"result": 9}, {"preview_url": "https://localhost/a"}]:
            self.assertIsNone(previews.validate_metadata(base | update, 123))

    def test_catalog_rejects_duplicates_unsafe_ids_and_unbounded_input(self):
        for maps in [[{"DisplayName": "X", "MapId": "../evil"}],
                     [{"DisplayName": "X", "MapId": "de_a", "WorkshopId": True}],
                     [{"DisplayName": "X", "MapId": "de_a"}] * 2,
                     [{"DisplayName": "X", "MapId": f"de_{i}"} for i in range(129)]]:
            with self.assertRaises(ValueError):
                previews.validate_maps({"Maps": maps})

    def test_cached_metadata_and_offline_fallback_do_not_make_requests(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root)
            catalog = {"Maps": [{"DisplayName": "Keep Configured Name", "MapId": "custom", "WorkshopId": 123},
                                {"DisplayName": "Default", "MapId": "de_dust2"}]}
            maps = path / "maps.json"
            maps.write_text(json.dumps(catalog))
            cache = path / "cache"
            cache.mkdir()
            data = {"publishedfileid": "123", "result": 1, "consumer_app_id": 730,
                    "title": "Actual Workshop Map", "preview_url": "https://images.steamusercontent.com/ugc/1/a"}
            (cache / "123.json").write_text(json.dumps({"checked_at": 100, "metadata": data}))
            with patch.object(previews, "request_bytes", side_effect=AssertionError("Unexpected network")):
                result = previews.prepare(maps, path / "prepared", cache, offline=True)
            self.assertEqual(2, len(result))
            self.assertEqual("custom", result[0]["MapId"])
            self.assertEqual("Keep Configured Name", result[0]["DisplayName"])
            self.assertEqual("Actual Workshop Map", result[0]["WorkshopTitle"])
            self.assertFalse(result[0]["HasImage"])
            self.assertFalse(result[1]["HasImage"])
            self.assertNotIn("background-image", (path / "prepared/ano_veto_previews.css").read_text())
            with self.assertRaises(ValueError):
                previews.prepare(maps, path / "prepared", cache, offline=True)

    def test_prepare_downloads_real_preview_and_writes_only_local_resource(self):
        from io import BytesIO
        from PIL import Image
        stream = BytesIO()
        Image.new("RGB", (40, 20), (12, 34, 56)).save(stream, format="JPEG")
        metadata = {"publishedfileid": "123", "result": 1, "consumer_app_id": 730,
                    "title": "Actual", "preview_url": "https://images.steamusercontent.com/ugc/1/a"}
        response = json.dumps({"response": {"publishedfiledetails": [metadata]}}).encode()
        with tempfile.TemporaryDirectory() as root:
            path = Path(root)
            maps = path / "maps.json"
            maps.write_text(json.dumps({"Maps": [{"DisplayName": "Configured", "MapId": "custom", "WorkshopId": 123}]}))
            with patch.object(previews, "request_bytes", side_effect=[response, stream.getvalue()]) as request:
                result = previews.prepare(maps, path / "prepared", path / "cache")
            self.assertTrue(result[0]["HasImage"])
            self.assertEqual(2, request.call_count)
            css = (path / "prepared/ano_veto_previews.css").read_text()
            self.assertIn(".ano_preview_w_123", css)
            self.assertIn("s2r://panorama/styles/custom_game/anocore/previews/ano_preview_w_123.vtex", css)
            self.assertNotIn("https:", css)
            self.assertTrue((path / "prepared/previews/ano_preview_w_123.png").is_file())
            # Cached image and metadata can be used for another build without contacting Steam.
            with patch.object(previews, "request_bytes", side_effect=AssertionError("Unexpected network")):
                self.assertTrue(previews.prepare(maps, path / "second", path / "cache", offline=True)[0]["HasImage"])

    def test_malformed_service_response_falls_back_without_changing_catalog(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root)
            maps = path / "maps.json"
            maps.write_text(json.dumps({"Maps": [{"DisplayName": "Configured", "MapId": "custom", "WorkshopId": 123}]}))
            with patch.object(previews, "request_bytes", return_value=b"not JSON"):
                result = previews.prepare(maps, path / "prepared", path / "cache")
            self.assertFalse(result[0]["HasImage"])
            self.assertEqual("custom", result[0]["MapId"])

    def test_standard_image_is_explicit_and_cannot_be_used_for_workshop(self):
        from PIL import Image
        with tempfile.TemporaryDirectory() as root:
            path = Path(root)
            Image.new("RGB", (12, 8), "red").save(path / "dust2.png")
            maps = path / "maps.json"
            maps.write_text(json.dumps({"Maps": [{"DisplayName": "Dust II", "MapId": "de_dust2"},
                                                {"DisplayName": "Custom", "MapId": "custom", "WorkshopId": 123}]}))
            images = path / "images.json"
            images.write_text(json.dumps({"de_dust2": "dust2.png"}))
            with patch.object(previews, "request_bytes", side_effect=AssertionError("Unexpected network")):
                result = previews.prepare(maps, path / "prepared", path / "cache", offline=True, standard_images=images)
            self.assertTrue(result[0]["HasImage"])
            self.assertFalse(result[1]["HasImage"])
            images.write_text(json.dumps({"custom": "dust2.png"}))
            with self.assertRaises(ValueError):
                previews.prepare(maps, path / "bad", path / "cache", offline=True, standard_images=images)
            self.assertFalse((path / "bad").exists())

    def test_negative_cache_and_invalid_preview_decode_fall_back(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root)
            cache = path / "cache"
            cache.mkdir()
            maps = path / "maps.json"
            maps.write_text(json.dumps({"Maps": [{"DisplayName": "Custom", "MapId": "custom", "WorkshopId": 123}]}))
            metadata = {"publishedfileid": "123", "result": 1, "consumer_app_id": 730,
                        "title": "Actual", "preview_url": "https://images.steamusercontent.com/ugc/1/a"}
            body = json.dumps({"response": {"publishedfiledetails": [metadata]}}).encode()
            with patch.object(previews, "request_bytes", side_effect=[body, b"bad image"]):
                self.assertFalse(previews.prepare(maps, path / "bad-image", cache)[0]["HasImage"])
            (cache / "123.json").write_text(json.dumps({"checked_at": previews.time.time(), "metadata": None}))
            with patch.object(previews, "request_bytes", side_effect=AssertionError("Negative cache ignored")):
                self.assertFalse(previews.prepare(maps, path / "cached-failure", cache)[0]["HasImage"])

    def test_network_budget_and_response_size_are_bounded(self):
        with self.assertRaises(TimeoutError):
            previews.request_bytes(previews.ENDPOINT, 10, previews.time.monotonic() - 1)
        from io import BytesIO
        class Opener:
            def open(self, *args, **kwargs):
                return BytesIO(b"x" * 11)
        with patch.object(previews.urllib.request, "build_opener", return_value=Opener()):
            with self.assertRaises(ValueError):
                previews.request_bytes(previews.ENDPOINT, 10, previews.time.monotonic() + 1)

    def test_output_and_cache_must_not_overlap(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root)
            with self.assertRaises(ValueError):
                previews.prepare(path / "maps.json", path / "prepared", path / "prepared/cache")
            self.assertFalse((path / "prepared").exists())


if __name__ == "__main__":
    unittest.main()
