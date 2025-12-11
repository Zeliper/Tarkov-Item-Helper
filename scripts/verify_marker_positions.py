#!/usr/bin/env python3
"""
Tarkov Market Marker Position Verification Script

Tarkov Market API에서 가져온 마커 데이터와 웹사이트에서 추출한 마커 위치를
교차 검증하는 스크립트입니다.

Usage:
    # 특정 맵 검증
    python verify_marker_positions.py --map woods --verbose

    # 특정 마커 검증
    python verify_marker_positions.py --marker-id ae6c753c-757b-41da-a28e-9eb5fd2d38aa

    # 전체 맵 검증 및 HTML 리포트 생성
    python verify_marker_positions.py --all --report html

    # 퀘스트 마커만 검증
    python verify_marker_positions.py --map customs --category Quests

Requirements:
    pip install playwright httpx pandas jinja2
    playwright install chromium
"""

import argparse
import asyncio
import base64
import json
import logging
import sqlite3
import sys
import urllib.parse
from dataclasses import dataclass, field
from datetime import datetime
from math import sqrt
from pathlib import Path
from typing import Optional

import httpx

# Optional imports - will gracefully degrade if not available
try:
    from playwright.async_api import async_playwright
    PLAYWRIGHT_AVAILABLE = True
except ImportError:
    PLAYWRIGHT_AVAILABLE = False
    print("Warning: playwright not installed. Web verification will be skipped.")

try:
    import pandas as pd
    PANDAS_AVAILABLE = True
except ImportError:
    PANDAS_AVAILABLE = False

try:
    from jinja2 import Template
    JINJA_AVAILABLE = True
except ImportError:
    JINJA_AVAILABLE = False

# Setup logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

# Constants
TARKOV_MARKET_BASE_URL = "https://tarkov-market.com"
MARKERS_API_URL = f"{TARKOV_MARKET_BASE_URL}/api/be/markers/list"
QUESTS_API_URL = f"{TARKOV_MARKET_BASE_URL}/api/be/quests/list"
SUPPORTED_MAPS = [
    "customs", "factory", "interchange", "labs", "lighthouse",
    "reserve", "shoreline", "streets", "woods", "ground-zero"
]
DEFAULT_TOLERANCE = 5.0  # SVG viewBox coordinate units


@dataclass
class MarkerPosition:
    """마커 위치 데이터"""
    uid: str
    name: str
    x: float
    y: float
    category: str = ""
    sub_category: str = ""
    map_name: str = ""
    level: Optional[int] = None


@dataclass
class VerificationResult:
    """검증 결과"""
    marker_uid: str
    marker_name: str
    map_name: str
    api_x: float
    api_y: float
    web_x: Optional[float] = None
    web_y: Optional[float] = None
    distance: Optional[float] = None
    is_match: bool = False
    error: Optional[str] = None
    screenshot_path: Optional[str] = None
    verified_at: datetime = field(default_factory=datetime.now)


class TarkovMarketDecoder:
    """Tarkov Market API 응답 디코딩"""

    @staticmethod
    def decode(encoded: str) -> Optional[str]:
        """
        난독화된 Base64 문자열 디코딩

        알고리즘:
        1. index 5-9 (5글자) 제거
        2. Base64 디코드
        3. URL 디코드
        """
        try:
            # 1. Remove characters at index 5-9
            processed = encoded[:5] + encoded[10:]

            # 2. Base64 decode
            decoded_bytes = base64.b64decode(processed)
            url_encoded = decoded_bytes.decode('utf-8')

            # 3. URL decode
            json_str = urllib.parse.unquote(url_encoded)
            return json_str
        except Exception as e:
            logger.error(f"Decoding error: {e}")
            return None


class MarkerVerifier:
    """마커 위치 검증 클래스"""

    def __init__(self, db_path: Optional[str] = None):
        self.api_markers: dict[str, list[MarkerPosition]] = {}
        self.web_markers: dict[str, list[MarkerPosition]] = {}
        self.results: list[VerificationResult] = []
        self.db_path = db_path or str(
            Path(__file__).parent.parent / "TarkovHelper" / "Data" / "tarkov_markers.db"
        )

    async def fetch_api_markers(self, map_name: str) -> list[MarkerPosition]:
        """API에서 마커 데이터 가져오기"""
        logger.info(f"Fetching API markers for {map_name}...")

        async with httpx.AsyncClient(timeout=30.0) as client:
            response = await client.get(
                MARKERS_API_URL,
                params={"map": map_name},
                headers={
                    "User-Agent": "TarkovHelper/1.0",
                    "Accept": "application/json"
                }
            )
            response.raise_for_status()
            data = response.json()

            # Decode obfuscated markers
            encoded_markers = data.get("markers", "")
            if not encoded_markers:
                logger.warning(f"No markers found for {map_name}")
                return []

            decoded_json = TarkovMarketDecoder.decode(encoded_markers)
            if not decoded_json:
                logger.error(f"Failed to decode markers for {map_name}")
                return []

            markers_data = json.loads(decoded_json)
            markers = []

            for m in markers_data:
                geometry = m.get("geometry", {})
                if geometry:
                    markers.append(MarkerPosition(
                        uid=m.get("uid", ""),
                        name=m.get("name", ""),
                        x=geometry.get("x", 0),
                        y=geometry.get("y", 0),
                        category=m.get("category", ""),
                        sub_category=m.get("subCategory", ""),
                        map_name=map_name,
                        level=m.get("level")
                    ))

            logger.info(f"Fetched {len(markers)} markers from API for {map_name}")
            self.api_markers[map_name] = markers
            return markers

    async def extract_web_markers(self, map_name: str) -> list[MarkerPosition]:
        """웹 페이지에서 마커 위치 추출"""
        if not PLAYWRIGHT_AVAILABLE:
            logger.warning("Playwright not available, skipping web extraction")
            return []

        logger.info(f"Extracting web markers for {map_name}...")
        markers = []

        async with async_playwright() as p:
            browser = await p.chromium.launch(headless=True)
            context = await browser.new_context(
                viewport={"width": 1920, "height": 1080}
            )
            page = await context.new_page()

            try:
                # Navigate to map page
                url = f"{TARKOV_MARKET_BASE_URL}/maps/{map_name}"
                await page.goto(url, wait_until="networkidle", timeout=60000)

                # Wait for markers to load
                await page.wait_for_timeout(3000)

                # Extract marker data from DOM/SVG
                # Tarkov Market uses Leaflet with SVG overlays
                markers_data = await page.evaluate('''() => {
                    const markers = [];

                    // Try to find marker elements
                    // Method 1: Look for Leaflet markers
                    const leafletMarkers = document.querySelectorAll('.leaflet-marker-icon');
                    leafletMarkers.forEach((el, idx) => {
                        const transform = el.style.transform;
                        const match = transform.match(/translate3d\(([^,]+)px,\s*([^,]+)px/);
                        if (match) {
                            markers.push({
                                index: idx,
                                x: parseFloat(match[1]),
                                y: parseFloat(match[2]),
                                source: 'leaflet'
                            });
                        }
                    });

                    // Method 2: Look for SVG elements with marker data
                    const svgMarkers = document.querySelectorAll('svg circle, svg g[data-id]');
                    svgMarkers.forEach(el => {
                        const cx = el.getAttribute('cx') || el.dataset.x;
                        const cy = el.getAttribute('cy') || el.dataset.y;
                        const id = el.dataset.id || el.id;
                        if (cx && cy) {
                            markers.push({
                                id: id,
                                x: parseFloat(cx),
                                y: parseFloat(cy),
                                source: 'svg'
                            });
                        }
                    });

                    // Method 3: Check for marker data in window objects
                    if (window.__NUXT__?.data) {
                        const nuxtMarkers = window.__NUXT__.data.markers || [];
                        nuxtMarkers.forEach(m => {
                            if (m.geometry) {
                                markers.push({
                                    id: m.uid,
                                    name: m.name,
                                    x: m.geometry.x,
                                    y: m.geometry.y,
                                    source: 'nuxt'
                                });
                            }
                        });
                    }

                    return markers;
                }''')

                for m in markers_data:
                    markers.append(MarkerPosition(
                        uid=m.get('id', f"web_{m.get('index', 0)}"),
                        name=m.get('name', ''),
                        x=m.get('x', 0),
                        y=m.get('y', 0),
                        map_name=map_name
                    ))

                logger.info(f"Extracted {len(markers)} markers from web for {map_name}")

            except Exception as e:
                logger.error(f"Error extracting web markers: {e}")
            finally:
                await browser.close()

        self.web_markers[map_name] = markers
        return markers

    def compare_positions(
        self,
        map_name: str,
        tolerance: float = DEFAULT_TOLERANCE,
        category_filter: Optional[str] = None
    ) -> list[VerificationResult]:
        """API와 웹 좌표 비교"""
        logger.info(f"Comparing positions for {map_name} (tolerance: {tolerance})...")

        api_markers = self.api_markers.get(map_name, [])
        web_markers = self.web_markers.get(map_name, [])

        # Apply category filter
        if category_filter:
            api_markers = [m for m in api_markers if m.category == category_filter]

        results = []

        # If no web markers, just report API markers as unverified
        if not web_markers:
            logger.warning(f"No web markers for {map_name}, marking all as unverified")
            for api_marker in api_markers:
                results.append(VerificationResult(
                    marker_uid=api_marker.uid,
                    marker_name=api_marker.name,
                    map_name=map_name,
                    api_x=api_marker.x,
                    api_y=api_marker.y,
                    error="No web markers available for comparison"
                ))
            self.results.extend(results)
            return results

        # Build web markers lookup by UID
        web_lookup = {m.uid: m for m in web_markers if m.uid}

        for api_marker in api_markers:
            result = VerificationResult(
                marker_uid=api_marker.uid,
                marker_name=api_marker.name,
                map_name=map_name,
                api_x=api_marker.x,
                api_y=api_marker.y
            )

            # Try exact UID match first
            if api_marker.uid in web_lookup:
                web_marker = web_lookup[api_marker.uid]
                result.web_x = web_marker.x
                result.web_y = web_marker.y
                result.distance = sqrt(
                    (api_marker.x - web_marker.x) ** 2 +
                    (api_marker.y - web_marker.y) ** 2
                )
                result.is_match = result.distance <= tolerance
            else:
                # Try nearest neighbor matching
                min_distance = float('inf')
                nearest_web = None

                for web_marker in web_markers:
                    dist = sqrt(
                        (api_marker.x - web_marker.x) ** 2 +
                        (api_marker.y - web_marker.y) ** 2
                    )
                    if dist < min_distance:
                        min_distance = dist
                        nearest_web = web_marker

                if nearest_web and min_distance <= tolerance * 2:
                    result.web_x = nearest_web.x
                    result.web_y = nearest_web.y
                    result.distance = min_distance
                    result.is_match = min_distance <= tolerance
                else:
                    result.error = "No matching web marker found"

            results.append(result)

        # Statistics
        total = len(results)
        matched = sum(1 for r in results if r.is_match)
        unmatched = sum(1 for r in results if not r.is_match and r.web_x is not None)
        missing = sum(1 for r in results if r.error)

        logger.info(f"Results for {map_name}: {matched}/{total} matched, "
                    f"{unmatched} discrepancies, {missing} missing")

        self.results.extend(results)
        return results

    async def verify_single_marker(
        self,
        marker_uid: str,
        take_screenshot: bool = False
    ) -> Optional[VerificationResult]:
        """단일 마커 검증"""
        if not PLAYWRIGHT_AVAILABLE:
            logger.error("Playwright required for single marker verification")
            return None

        # First, find which map this marker belongs to
        marker_info = None
        marker_map = None

        for map_name in SUPPORTED_MAPS:
            if map_name not in self.api_markers:
                await self.fetch_api_markers(map_name)

            for marker in self.api_markers.get(map_name, []):
                if marker.uid == marker_uid:
                    marker_info = marker
                    marker_map = map_name
                    break

            if marker_info:
                break

        if not marker_info:
            logger.error(f"Marker {marker_uid} not found in API data")
            return None

        logger.info(f"Verifying marker '{marker_info.name}' on {marker_map}")

        result = VerificationResult(
            marker_uid=marker_uid,
            marker_name=marker_info.name,
            map_name=marker_map,
            api_x=marker_info.x,
            api_y=marker_info.y
        )

        async with async_playwright() as p:
            browser = await p.chromium.launch(headless=not take_screenshot)
            page = await browser.new_page(viewport={"width": 1920, "height": 1080})

            try:
                url = f"{TARKOV_MARKET_BASE_URL}/maps/{marker_map}"
                await page.goto(url, wait_until="networkidle", timeout=60000)
                await page.wait_for_timeout(3000)

                # Try to find and highlight the specific marker
                found = await page.evaluate(f'''(markerId) => {{
                    // Search in various data sources
                    const el = document.querySelector(`[data-id="${{markerId}}"]`);
                    if (el) {{
                        el.scrollIntoView({{behavior: 'smooth', block: 'center'}});
                        el.style.border = '3px solid red';
                        return true;
                    }}
                    return false;
                }}''', marker_uid)

                if take_screenshot:
                    screenshots_dir = Path(__file__).parent / "verification_screenshots"
                    screenshots_dir.mkdir(exist_ok=True)
                    screenshot_path = screenshots_dir / f"{marker_uid}.png"
                    await page.screenshot(path=str(screenshot_path))
                    result.screenshot_path = str(screenshot_path)
                    logger.info(f"Screenshot saved: {screenshot_path}")

                if not found:
                    result.error = "Marker element not found in DOM"

            except Exception as e:
                result.error = str(e)
                logger.error(f"Verification error: {e}")
            finally:
                await browser.close()

        return result

    def save_to_sqlite(self):
        """검증 결과를 SQLite에 저장"""
        logger.info(f"Saving {len(self.results)} results to SQLite...")

        # Ensure directory exists
        db_dir = Path(self.db_path).parent
        db_dir.mkdir(parents=True, exist_ok=True)

        conn = sqlite3.connect(self.db_path)
        cursor = conn.cursor()

        # Create table if not exists
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS verification_results (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                marker_uid TEXT NOT NULL,
                marker_name TEXT,
                map_name TEXT,
                verified_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                api_x REAL,
                api_y REAL,
                web_x REAL,
                web_y REAL,
                distance REAL,
                is_match BOOLEAN,
                error TEXT,
                screenshot_path TEXT
            )
        ''')

        # Create index
        cursor.execute('''
            CREATE INDEX IF NOT EXISTS idx_verification_marker
            ON verification_results(marker_uid)
        ''')

        # Insert results
        for result in self.results:
            cursor.execute('''
                INSERT INTO verification_results
                (marker_uid, marker_name, map_name, verified_at,
                 api_x, api_y, web_x, web_y, distance, is_match, error, screenshot_path)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ''', (
                result.marker_uid,
                result.marker_name,
                result.map_name,
                result.verified_at.isoformat(),
                result.api_x,
                result.api_y,
                result.web_x,
                result.web_y,
                result.distance,
                result.is_match,
                result.error,
                result.screenshot_path
            ))

        conn.commit()
        conn.close()
        logger.info(f"Results saved to {self.db_path}")

    def generate_report(self, format: str = "json") -> str:
        """검증 리포트 생성"""
        if format == "json":
            return self._generate_json_report()
        elif format == "html":
            return self._generate_html_report()
        elif format == "csv" and PANDAS_AVAILABLE:
            return self._generate_csv_report()
        else:
            return self._generate_json_report()

    def _generate_json_report(self) -> str:
        """JSON 리포트 생성"""
        report = {
            "generated_at": datetime.now().isoformat(),
            "total_markers": len(self.results),
            "matched": sum(1 for r in self.results if r.is_match),
            "discrepancies": sum(1 for r in self.results if not r.is_match and r.distance),
            "missing": sum(1 for r in self.results if r.error),
            "by_map": {},
            "discrepancy_details": []
        }

        # Group by map
        for result in self.results:
            map_name = result.map_name
            if map_name not in report["by_map"]:
                report["by_map"][map_name] = {
                    "total": 0, "matched": 0, "discrepancies": 0, "missing": 0
                }

            report["by_map"][map_name]["total"] += 1
            if result.is_match:
                report["by_map"][map_name]["matched"] += 1
            elif result.distance:
                report["by_map"][map_name]["discrepancies"] += 1
                report["discrepancy_details"].append({
                    "uid": result.marker_uid,
                    "name": result.marker_name,
                    "map": result.map_name,
                    "api": {"x": result.api_x, "y": result.api_y},
                    "web": {"x": result.web_x, "y": result.web_y},
                    "distance": result.distance
                })
            else:
                report["by_map"][map_name]["missing"] += 1

        # Save to file
        report_path = Path(__file__).parent / "verification_report.json"
        with open(report_path, 'w', encoding='utf-8') as f:
            json.dump(report, f, indent=2, ensure_ascii=False)

        logger.info(f"JSON report saved: {report_path}")
        return str(report_path)

    def _generate_html_report(self) -> str:
        """HTML 리포트 생성"""
        if not JINJA_AVAILABLE:
            logger.warning("jinja2 not available, falling back to JSON report")
            return self._generate_json_report()

        template = Template('''
<!DOCTYPE html>
<html>
<head>
    <title>Tarkov Market Marker Verification Report</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; background: #1a1a1a; color: #fff; }
        h1 { color: #ffd700; }
        .summary { background: #2a2a2a; padding: 20px; border-radius: 8px; margin-bottom: 20px; }
        .stat { display: inline-block; margin-right: 30px; }
        .stat-value { font-size: 24px; font-weight: bold; color: #4caf50; }
        .stat-label { color: #888; }
        table { width: 100%; border-collapse: collapse; background: #2a2a2a; }
        th, td { padding: 10px; text-align: left; border-bottom: 1px solid #444; }
        th { background: #333; color: #ffd700; }
        .match { color: #4caf50; }
        .mismatch { color: #f44336; }
        .missing { color: #ff9800; }
    </style>
</head>
<body>
    <h1>Marker Verification Report</h1>
    <p>Generated: {{ generated_at }}</p>

    <div class="summary">
        <div class="stat">
            <div class="stat-value">{{ total }}</div>
            <div class="stat-label">Total Markers</div>
        </div>
        <div class="stat">
            <div class="stat-value match">{{ matched }}</div>
            <div class="stat-label">Matched</div>
        </div>
        <div class="stat">
            <div class="stat-value mismatch">{{ discrepancies }}</div>
            <div class="stat-label">Discrepancies</div>
        </div>
        <div class="stat">
            <div class="stat-value missing">{{ missing_count }}</div>
            <div class="stat-label">Missing</div>
        </div>
    </div>

    <h2>Results by Map</h2>
    <table>
        <tr>
            <th>Map</th>
            <th>Total</th>
            <th>Matched</th>
            <th>Discrepancies</th>
            <th>Missing</th>
            <th>Match Rate</th>
        </tr>
        {% for map_name, stats in by_map.items() %}
        <tr>
            <td>{{ map_name }}</td>
            <td>{{ stats.total }}</td>
            <td class="match">{{ stats.matched }}</td>
            <td class="mismatch">{{ stats.discrepancies }}</td>
            <td class="missing">{{ stats.missing }}</td>
            <td>{{ "%.1f"|format(stats.matched / stats.total * 100 if stats.total else 0) }}%</td>
        </tr>
        {% endfor %}
    </table>

    {% if discrepancy_details %}
    <h2>Discrepancy Details</h2>
    <table>
        <tr>
            <th>Marker</th>
            <th>Map</th>
            <th>API Position</th>
            <th>Web Position</th>
            <th>Distance</th>
        </tr>
        {% for d in discrepancy_details %}
        <tr>
            <td>{{ d.name }}</td>
            <td>{{ d.map }}</td>
            <td>({{ "%.2f"|format(d.api.x) }}, {{ "%.2f"|format(d.api.y) }})</td>
            <td>({{ "%.2f"|format(d.web.x) }}, {{ "%.2f"|format(d.web.y) }})</td>
            <td class="mismatch">{{ "%.2f"|format(d.distance) }}</td>
        </tr>
        {% endfor %}
    </table>
    {% endif %}
</body>
</html>
        ''')

        # Prepare data
        total = len(self.results)
        matched = sum(1 for r in self.results if r.is_match)
        discrepancies = sum(1 for r in self.results if not r.is_match and r.distance)
        missing_count = sum(1 for r in self.results if r.error)

        by_map = {}
        discrepancy_details = []

        for result in self.results:
            map_name = result.map_name
            if map_name not in by_map:
                by_map[map_name] = {"total": 0, "matched": 0, "discrepancies": 0, "missing": 0}

            by_map[map_name]["total"] += 1
            if result.is_match:
                by_map[map_name]["matched"] += 1
            elif result.distance:
                by_map[map_name]["discrepancies"] += 1
                discrepancy_details.append({
                    "uid": result.marker_uid,
                    "name": result.marker_name,
                    "map": result.map_name,
                    "api": {"x": result.api_x, "y": result.api_y},
                    "web": {"x": result.web_x, "y": result.web_y},
                    "distance": result.distance
                })
            else:
                by_map[map_name]["missing"] += 1

        html = template.render(
            generated_at=datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
            total=total,
            matched=matched,
            discrepancies=discrepancies,
            missing_count=missing_count,
            by_map=by_map,
            discrepancy_details=discrepancy_details[:50]  # Limit to 50
        )

        report_path = Path(__file__).parent / "verification_report.html"
        with open(report_path, 'w', encoding='utf-8') as f:
            f.write(html)

        logger.info(f"HTML report saved: {report_path}")
        return str(report_path)

    def _generate_csv_report(self) -> str:
        """CSV 리포트 생성"""
        df = pd.DataFrame([
            {
                "marker_uid": r.marker_uid,
                "marker_name": r.marker_name,
                "map": r.map_name,
                "api_x": r.api_x,
                "api_y": r.api_y,
                "web_x": r.web_x,
                "web_y": r.web_y,
                "distance": r.distance,
                "is_match": r.is_match,
                "error": r.error
            }
            for r in self.results
        ])

        report_path = Path(__file__).parent / "verification_report.csv"
        df.to_csv(report_path, index=False, encoding='utf-8')
        logger.info(f"CSV report saved: {report_path}")
        return str(report_path)


async def main():
    parser = argparse.ArgumentParser(
        description="Verify Tarkov Market marker positions"
    )
    parser.add_argument(
        "--map", "-m",
        choices=SUPPORTED_MAPS,
        help="Map name to verify"
    )
    parser.add_argument(
        "--all", "-a",
        action="store_true",
        help="Verify all maps"
    )
    parser.add_argument(
        "--marker-id",
        help="Verify a single marker by UID"
    )
    parser.add_argument(
        "--category", "-c",
        choices=["Quests", "Extractions", "Spawns", "Keys", "Loot"],
        help="Filter by marker category"
    )
    parser.add_argument(
        "--tolerance", "-t",
        type=float,
        default=DEFAULT_TOLERANCE,
        help=f"Position tolerance (default: {DEFAULT_TOLERANCE})"
    )
    parser.add_argument(
        "--report", "-r",
        choices=["json", "html", "csv"],
        default="json",
        help="Report format (default: json)"
    )
    parser.add_argument(
        "--save-db",
        action="store_true",
        help="Save results to SQLite database"
    )
    parser.add_argument(
        "--screenshot",
        action="store_true",
        help="Take screenshots (for single marker verification)"
    )
    parser.add_argument(
        "--verbose", "-v",
        action="store_true",
        help="Verbose output"
    )

    args = parser.parse_args()

    if args.verbose:
        logging.getLogger().setLevel(logging.DEBUG)

    verifier = MarkerVerifier()

    # Single marker verification
    if args.marker_id:
        result = await verifier.verify_single_marker(
            args.marker_id,
            take_screenshot=args.screenshot
        )
        if result:
            print(f"\nVerification Result:")
            print(f"  Marker: {result.marker_name}")
            print(f"  Map: {result.map_name}")
            print(f"  API Position: ({result.api_x:.2f}, {result.api_y:.2f})")
            if result.web_x is not None:
                print(f"  Web Position: ({result.web_x:.2f}, {result.web_y:.2f})")
                print(f"  Distance: {result.distance:.2f}")
                print(f"  Match: {'Yes' if result.is_match else 'No'}")
            if result.error:
                print(f"  Error: {result.error}")
            if result.screenshot_path:
                print(f"  Screenshot: {result.screenshot_path}")
        return

    # Map verification
    maps_to_verify = SUPPORTED_MAPS if args.all else ([args.map] if args.map else [])

    if not maps_to_verify:
        parser.print_help()
        return

    for map_name in maps_to_verify:
        # Fetch API markers
        await verifier.fetch_api_markers(map_name)

        # Extract web markers (if playwright available)
        if PLAYWRIGHT_AVAILABLE:
            await verifier.extract_web_markers(map_name)

        # Compare
        verifier.compare_positions(
            map_name,
            tolerance=args.tolerance,
            category_filter=args.category
        )

    # Generate report
    report_path = verifier.generate_report(args.report)
    print(f"\nReport generated: {report_path}")

    # Save to SQLite if requested
    if args.save_db:
        verifier.save_to_sqlite()

    # Print summary
    total = len(verifier.results)
    matched = sum(1 for r in verifier.results if r.is_match)
    print(f"\nSummary: {matched}/{total} markers verified ({matched/total*100:.1f}%)")


if __name__ == "__main__":
    asyncio.run(main())
