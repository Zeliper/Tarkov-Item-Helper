#!/usr/bin/env python3
"""
Tarkov Market Marker SQLite Sync Script

Tarkov Market API에서 마커 및 퀘스트 데이터를 가져와
SQLite 데이터베이스에 동기화하는 스크립트입니다.

Usage:
    # 전체 동기화 (모든 맵)
    python sync_markers_to_sqlite.py --full

    # 특정 맵만 동기화
    python sync_markers_to_sqlite.py --map woods

    # 퀘스트 데이터만 동기화
    python sync_markers_to_sqlite.py --quests-only

    # 데이터베이스 초기화 (스키마 재생성)
    python sync_markers_to_sqlite.py --init-db

    # 통계 확인
    python sync_markers_to_sqlite.py --stats

Requirements:
    pip install httpx
"""

import argparse
import asyncio
import base64
import json
import logging
import sqlite3
import urllib.parse
from datetime import datetime
from pathlib import Path
from typing import Optional

import httpx

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

# Default database path
DEFAULT_DB_PATH = Path(__file__).parent.parent / "TarkovHelper" / "Data" / "tarkov_markers.db"


# SQL Schema
SCHEMA_SQL = '''
-- =============================================
-- Tarkov Market Marker Database Schema
-- Version: 1.0
-- Created: 2025-12-11
-- =============================================

-- 마커 테이블: Tarkov Market에서 가져온 모든 마커
CREATE TABLE IF NOT EXISTS markers (
    uid TEXT PRIMARY KEY,
    map TEXT NOT NULL,
    category TEXT NOT NULL,
    sub_category TEXT,
    name TEXT NOT NULL,
    name_ko TEXT,
    name_ru TEXT,
    description TEXT,
    description_ko TEXT,
    geometry_x REAL NOT NULL,
    geometry_y REAL NOT NULL,
    level INTEGER,
    quest_uid TEXT,
    items_uid TEXT,  -- JSON array
    images TEXT,     -- JSON array
    updated_at DATETIME,
    synced_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    is_verified BOOLEAN DEFAULT FALSE,
    verification_distance REAL,

    FOREIGN KEY (quest_uid) REFERENCES quests(uid)
);

-- 퀘스트 테이블: Tarkov Market 퀘스트 데이터
CREATE TABLE IF NOT EXISTS quests (
    uid TEXT PRIMARY KEY,
    bsg_id TEXT UNIQUE NOT NULL,
    name TEXT NOT NULL,
    name_ru TEXT,
    name_ko TEXT,
    trader TEXT,
    type TEXT,
    wiki_url TEXT,
    required_level INTEGER,
    required_loyalty_level INTEGER,
    required_reputation REAL,
    required_for_kappa BOOLEAN DEFAULT FALSE,
    is_active BOOLEAN DEFAULT TRUE,
    objectives_en TEXT,  -- JSON array
    objectives_ru TEXT,  -- JSON array
    updated_at DATETIME,
    synced_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 검증 결과 테이블
CREATE TABLE IF NOT EXISTS verification_results (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    marker_uid TEXT NOT NULL,
    verified_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    api_x REAL,
    api_y REAL,
    web_x REAL,
    web_y REAL,
    distance REAL,
    is_match BOOLEAN,
    screenshot_path TEXT,
    notes TEXT,

    FOREIGN KEY (marker_uid) REFERENCES markers(uid)
);

-- 동기화 메타데이터
CREATE TABLE IF NOT EXISTS sync_metadata (
    key TEXT PRIMARY KEY,
    value TEXT,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 마커 이미지 테이블 (정규화)
CREATE TABLE IF NOT EXISTS marker_images (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    marker_uid TEXT NOT NULL,
    image_url TEXT NOT NULL,
    image_name TEXT,
    image_description TEXT,
    display_order INTEGER DEFAULT 0,

    FOREIGN KEY (marker_uid) REFERENCES markers(uid)
);

-- 인덱스
CREATE INDEX IF NOT EXISTS idx_markers_map ON markers(map);
CREATE INDEX IF NOT EXISTS idx_markers_category ON markers(category);
CREATE INDEX IF NOT EXISTS idx_markers_quest ON markers(quest_uid);
CREATE INDEX IF NOT EXISTS idx_markers_verified ON markers(is_verified);
CREATE INDEX IF NOT EXISTS idx_quests_bsg ON quests(bsg_id);
CREATE INDEX IF NOT EXISTS idx_quests_trader ON quests(trader);
CREATE INDEX IF NOT EXISTS idx_quests_kappa ON quests(required_for_kappa);
CREATE INDEX IF NOT EXISTS idx_verification_marker ON verification_results(marker_uid);
CREATE INDEX IF NOT EXISTS idx_marker_images_marker ON marker_images(marker_uid);

-- 뷰: 퀘스트별 마커 수
CREATE VIEW IF NOT EXISTS v_quest_marker_counts AS
SELECT
    q.uid,
    q.bsg_id,
    q.name,
    q.trader,
    COUNT(m.uid) as marker_count
FROM quests q
LEFT JOIN markers m ON m.quest_uid = q.uid
GROUP BY q.uid;

-- 뷰: 맵별 마커 통계
CREATE VIEW IF NOT EXISTS v_map_marker_stats AS
SELECT
    map,
    category,
    COUNT(*) as count,
    SUM(CASE WHEN is_verified THEN 1 ELSE 0 END) as verified_count
FROM markers
GROUP BY map, category;

-- 뷰: 미검증 마커
CREATE VIEW IF NOT EXISTS v_unverified_markers AS
SELECT
    m.*,
    q.name as quest_name,
    q.trader
FROM markers m
LEFT JOIN quests q ON m.quest_uid = q.uid
WHERE m.is_verified = FALSE;
'''


class TarkovMarketDecoder:
    """Tarkov Market API 응답 디코딩"""

    @staticmethod
    def decode(encoded: str) -> Optional[str]:
        try:
            processed = encoded[:5] + encoded[10:]
            decoded_bytes = base64.b64decode(processed)
            url_encoded = decoded_bytes.decode('utf-8')
            json_str = urllib.parse.unquote(url_encoded)
            return json_str
        except Exception as e:
            logger.error(f"Decoding error: {e}")
            return None


class MarkerSyncService:
    """마커 SQLite 동기화 서비스"""

    def __init__(self, db_path: Optional[str] = None):
        self.db_path = str(db_path or DEFAULT_DB_PATH)
        self._ensure_db_directory()

    def _ensure_db_directory(self):
        """데이터베이스 디렉토리 생성"""
        Path(self.db_path).parent.mkdir(parents=True, exist_ok=True)

    def init_database(self):
        """데이터베이스 스키마 초기화"""
        logger.info(f"Initializing database at {self.db_path}")

        conn = sqlite3.connect(self.db_path)
        conn.executescript(SCHEMA_SQL)
        conn.commit()

        # Set initial metadata
        cursor = conn.cursor()
        cursor.execute('''
            INSERT OR REPLACE INTO sync_metadata (key, value, updated_at)
            VALUES ('schema_version', '1.0', datetime('now'))
        ''')
        conn.commit()
        conn.close()

        logger.info("Database initialized successfully")

    async def fetch_markers(self, map_name: str) -> list[dict]:
        """API에서 마커 데이터 가져오기"""
        logger.info(f"Fetching markers for {map_name}...")

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

            encoded_markers = data.get("markers", "")
            if not encoded_markers:
                return []

            decoded_json = TarkovMarketDecoder.decode(encoded_markers)
            if not decoded_json:
                return []

            return json.loads(decoded_json)

    async def fetch_quests(self) -> list[dict]:
        """API에서 퀘스트 데이터 가져오기"""
        logger.info("Fetching quests...")

        async with httpx.AsyncClient(timeout=30.0) as client:
            response = await client.get(
                QUESTS_API_URL,
                headers={
                    "User-Agent": "TarkovHelper/1.0",
                    "Accept": "application/json"
                }
            )
            response.raise_for_status()
            data = response.json()

            encoded_quests = data.get("quests", "")
            if not encoded_quests:
                return []

            decoded_json = TarkovMarketDecoder.decode(encoded_quests)
            if not decoded_json:
                return []

            return json.loads(decoded_json)

    def sync_markers(self, markers: list[dict], map_name: str):
        """마커 데이터를 SQLite에 동기화"""
        logger.info(f"Syncing {len(markers)} markers for {map_name}...")

        conn = sqlite3.connect(self.db_path)
        cursor = conn.cursor()

        synced_count = 0
        for marker in markers:
            geometry = marker.get("geometry", {})
            if not geometry:
                continue

            # Extract localization
            name_l10n = marker.get("name_l10n", {}) or {}
            desc_l10n = marker.get("desc_l10n", {}) or {}

            # Serialize arrays as JSON
            items_uid = json.dumps(marker.get("itemsUid")) if marker.get("itemsUid") else None
            images = json.dumps(marker.get("imgs")) if marker.get("imgs") else None

            cursor.execute('''
                INSERT OR REPLACE INTO markers (
                    uid, map, category, sub_category,
                    name, name_ko, name_ru, description, description_ko,
                    geometry_x, geometry_y, level, quest_uid,
                    items_uid, images, updated_at, synced_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now'))
            ''', (
                marker.get("uid"),
                map_name,
                marker.get("category"),
                marker.get("subCategory"),
                marker.get("name"),
                name_l10n.get("ko"),
                name_l10n.get("ru"),
                marker.get("desc"),
                desc_l10n.get("ko"),
                geometry.get("x"),
                geometry.get("y"),
                marker.get("level"),
                marker.get("questUid"),
                items_uid,
                images,
                marker.get("updated")
            ))

            # Sync images to separate table
            if marker.get("imgs"):
                cursor.execute('DELETE FROM marker_images WHERE marker_uid = ?', (marker.get("uid"),))
                for idx, img in enumerate(marker.get("imgs", [])):
                    cursor.execute('''
                        INSERT INTO marker_images (marker_uid, image_url, image_name, image_description, display_order)
                        VALUES (?, ?, ?, ?, ?)
                    ''', (
                        marker.get("uid"),
                        img.get("img"),
                        img.get("name"),
                        img.get("desc"),
                        idx
                    ))

            synced_count += 1

        conn.commit()
        conn.close()

        logger.info(f"Synced {synced_count} markers for {map_name}")
        return synced_count

    def sync_quests(self, quests: list[dict]):
        """퀘스트 데이터를 SQLite에 동기화"""
        logger.info(f"Syncing {len(quests)} quests...")

        conn = sqlite3.connect(self.db_path)
        cursor = conn.cursor()

        synced_count = 0
        for quest in quests:
            objectives_en = json.dumps(quest.get("enObjectives")) if quest.get("enObjectives") else None
            objectives_ru = json.dumps(quest.get("ruObjectives")) if quest.get("ruObjectives") else None

            cursor.execute('''
                INSERT OR REPLACE INTO quests (
                    uid, bsg_id, name, name_ru, trader, type,
                    wiki_url, required_level, required_loyalty_level,
                    required_reputation, required_for_kappa, is_active,
                    objectives_en, objectives_ru, updated_at, synced_at
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, datetime('now'))
            ''', (
                quest.get("uid"),
                quest.get("bsgId"),
                quest.get("name"),
                quest.get("ruName"),
                quest.get("trader"),
                quest.get("type"),
                quest.get("wikiUrl"),
                quest.get("reqLevel"),
                quest.get("reqLL"),
                quest.get("reqRep"),
                quest.get("requiredForKappa", False),
                quest.get("active", True),
                objectives_en,
                objectives_ru,
                quest.get("updated")
            ))
            synced_count += 1

        conn.commit()
        conn.close()

        logger.info(f"Synced {synced_count} quests")
        return synced_count

    def update_sync_metadata(self, key: str, value: str):
        """동기화 메타데이터 업데이트"""
        conn = sqlite3.connect(self.db_path)
        cursor = conn.cursor()
        cursor.execute('''
            INSERT OR REPLACE INTO sync_metadata (key, value, updated_at)
            VALUES (?, ?, datetime('now'))
        ''', (key, value))
        conn.commit()
        conn.close()

    def get_stats(self) -> dict:
        """데이터베이스 통계 조회"""
        conn = sqlite3.connect(self.db_path)
        cursor = conn.cursor()

        stats = {}

        # Total markers
        cursor.execute('SELECT COUNT(*) FROM markers')
        stats['total_markers'] = cursor.fetchone()[0]

        # Markers by map
        cursor.execute('SELECT map, COUNT(*) FROM markers GROUP BY map ORDER BY COUNT(*) DESC')
        stats['markers_by_map'] = dict(cursor.fetchall())

        # Markers by category
        cursor.execute('SELECT category, COUNT(*) FROM markers GROUP BY category ORDER BY COUNT(*) DESC')
        stats['markers_by_category'] = dict(cursor.fetchall())

        # Total quests
        cursor.execute('SELECT COUNT(*) FROM quests')
        stats['total_quests'] = cursor.fetchone()[0]

        # Kappa quests
        cursor.execute('SELECT COUNT(*) FROM quests WHERE required_for_kappa = TRUE')
        stats['kappa_quests'] = cursor.fetchone()[0]

        # Verified markers
        cursor.execute('SELECT COUNT(*) FROM markers WHERE is_verified = TRUE')
        stats['verified_markers'] = cursor.fetchone()[0]

        # Last sync time
        cursor.execute("SELECT value FROM sync_metadata WHERE key = 'last_full_sync'")
        row = cursor.fetchone()
        stats['last_full_sync'] = row[0] if row else None

        conn.close()
        return stats

    async def full_sync(self, maps: Optional[list[str]] = None):
        """전체 동기화 수행"""
        maps_to_sync = maps or SUPPORTED_MAPS

        # Initialize DB if needed
        if not Path(self.db_path).exists():
            self.init_database()

        total_markers = 0
        total_quests = 0

        # Sync markers for each map
        for map_name in maps_to_sync:
            try:
                markers = await self.fetch_markers(map_name)
                count = self.sync_markers(markers, map_name)
                total_markers += count
            except Exception as e:
                logger.error(f"Error syncing markers for {map_name}: {e}")

        # Sync quests
        try:
            quests = await self.fetch_quests()
            total_quests = self.sync_quests(quests)
        except Exception as e:
            logger.error(f"Error syncing quests: {e}")

        # Update metadata
        self.update_sync_metadata('last_full_sync', datetime.now().isoformat())
        self.update_sync_metadata('total_markers', str(total_markers))
        self.update_sync_metadata('total_quests', str(total_quests))

        logger.info(f"Full sync completed: {total_markers} markers, {total_quests} quests")
        return {
            'markers': total_markers,
            'quests': total_quests
        }


async def main():
    parser = argparse.ArgumentParser(
        description="Sync Tarkov Market markers to SQLite"
    )
    parser.add_argument(
        "--full", "-f",
        action="store_true",
        help="Perform full sync of all maps"
    )
    parser.add_argument(
        "--map", "-m",
        choices=SUPPORTED_MAPS,
        help="Sync specific map only"
    )
    parser.add_argument(
        "--quests-only", "-q",
        action="store_true",
        help="Sync quests only"
    )
    parser.add_argument(
        "--init-db",
        action="store_true",
        help="Initialize/reset database schema"
    )
    parser.add_argument(
        "--stats",
        action="store_true",
        help="Show database statistics"
    )
    parser.add_argument(
        "--db-path",
        type=str,
        help=f"Database path (default: {DEFAULT_DB_PATH})"
    )
    parser.add_argument(
        "--verbose", "-v",
        action="store_true",
        help="Verbose output"
    )

    args = parser.parse_args()

    if args.verbose:
        logging.getLogger().setLevel(logging.DEBUG)

    service = MarkerSyncService(args.db_path)

    # Initialize database
    if args.init_db:
        service.init_database()
        print("Database initialized successfully")
        return

    # Show stats
    if args.stats:
        if not Path(service.db_path).exists():
            print("Database does not exist. Run with --init-db first.")
            return

        stats = service.get_stats()
        print("\n=== Tarkov Markers Database Statistics ===\n")
        print(f"Total Markers: {stats['total_markers']}")
        print(f"Total Quests: {stats['total_quests']}")
        print(f"Kappa Quests: {stats['kappa_quests']}")
        print(f"Verified Markers: {stats['verified_markers']}")
        print(f"Last Full Sync: {stats['last_full_sync'] or 'Never'}")
        print("\nMarkers by Map:")
        for map_name, count in stats['markers_by_map'].items():
            print(f"  {map_name}: {count}")
        print("\nMarkers by Category:")
        for category, count in stats['markers_by_category'].items():
            print(f"  {category}: {count}")
        return

    # Quests only sync
    if args.quests_only:
        quests = await service.fetch_quests()
        count = service.sync_quests(quests)
        print(f"Synced {count} quests")
        return

    # Map sync
    if args.map:
        markers = await service.fetch_markers(args.map)
        count = service.sync_markers(markers, args.map)
        print(f"Synced {count} markers for {args.map}")
        return

    # Full sync
    if args.full:
        result = await service.full_sync()
        print(f"\nFull sync completed:")
        print(f"  Markers: {result['markers']}")
        print(f"  Quests: {result['quests']}")
        return

    # Default: show help
    parser.print_help()


if __name__ == "__main__":
    asyncio.run(main())
