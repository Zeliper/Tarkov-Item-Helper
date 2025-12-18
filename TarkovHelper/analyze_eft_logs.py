import os
import re
from datetime import datetime
from pathlib import Path

# 로그 폴더 경로
LOG_BASE_PATH = r"C:\Program Files (x86)\Steam\steamapps\common\Escape from Tarkov\build\Logs"

# 맵 ID -> 맵 이름 매핑
MAP_NAMES = {
    'factory4_day': 'Factory (Day)',
    'factory4_night': 'Factory (Night)',
    'bigmap': 'Customs',
    'Woods': 'Woods',
    'Shoreline': 'Shoreline',
    'Interchange': 'Interchange',
    'laboratory': 'The Lab',
    'RezervBase': 'Reserve',
    'lighthouse': 'Lighthouse',
    'tarkovstreets': 'Streets of Tarkov',
    'sandbox': 'Ground Zero',
    'sandbox_high': 'Ground Zero (High)'
}

def parse_timestamp(timestamp_str):
    """로그 타임스탬프를 파싱"""
    try:
        return datetime.strptime(timestamp_str, '%Y-%m-%d %H:%M:%S.%f')
    except:
        return None

def analyze_session(session_folder):
    """단일 세션 폴더 분석"""
    session_path = Path(LOG_BASE_PATH) / session_folder

    # 세션 시작 시간 추출 (폴더명에서)
    match = re.match(r'log_(\d{4})\.(\d{2})\.(\d{2})_(\d+)-(\d+)-(\d+)_', session_folder)
    if not match:
        return None

    year, month, day, hour, minute, second = match.groups()
    session_start = f"{year}-{month}-{day} {hour.zfill(2)}:{minute.zfill(2)}:{second.zfill(2)}"

    results = []

    # backend 로그에서 레이드 정보 추출
    backend_log = None
    for file in session_path.glob('*backend_000.log'):
        backend_log = file
        break

    if not backend_log or not backend_log.exists():
        return None

    # application 로그에서 PMC/Scav 정보 추출
    application_log = None
    for file in session_path.glob('*application_000.log'):
        application_log = file
        break

    raid_configs = []
    session_modes = {}

    # application 로그에서 세션 모드 추출
    if application_log and application_log.exists():
        try:
            with open(application_log, 'r', encoding='utf-8', errors='ignore') as f:
                for line in f:
                    # Session mode: Pve 또는 Pvp
                    if 'Session mode:' in line:
                        parts = line.split('|')
                        if len(parts) >= 5:
                            timestamp = parts[0].strip()
                            mode = 'PVE' if 'Pve' in line else 'PVP'
                            session_modes[timestamp] = mode
        except Exception as e:
            pass

    # backend 로그 분석
    try:
        with open(backend_log, 'r', encoding='utf-8', errors='ignore') as f:
            current_raid = None

            for line in f:
                # /client/raid/configuration 요청 찾기
                if '/client/raid/configuration' in line and '---> Request' in line:
                    parts = line.split('|')
                    if len(parts) >= 5:
                        timestamp_str = parts[0].strip()
                        timestamp = parse_timestamp(timestamp_str)

                        current_raid = {
                            'session_folder': session_folder,
                            'session_start': session_start,
                            'raid_start': timestamp_str,
                            'raid_start_dt': timestamp,
                            'map': 'Unknown',
                            'mode': 'Unknown',
                            'raid_end': None,
                            'raid_end_dt': None,
                            'duration_min': None,
                            'exit_status': 'N/A'
                        }

                # 응답에서 맵 정보 추출
                if current_raid and '<--- Response' in line and '/client/raid/configuration' in line:
                    # 다음 몇 줄에서 location 정보 찾기
                    pass

                # responseText에서 location 추출
                if current_raid and '"location":' in line:
                    match = re.search(r'"location":"([^"]+)"', line)
                    if match:
                        location_id = match.group(1)
                        current_raid['map'] = MAP_NAMES.get(location_id, location_id)

                # 세션 종료 찾기 (game/logout 또는 다른 레이드 시작)
                if current_raid and ('/client/game/logout' in line or
                                    ('/client/raid/configuration' in line and '---> Request' in line and
                                     current_raid.get('map') != 'Unknown')):
                    parts = line.split('|')
                    if len(parts) >= 5:
                        end_timestamp_str = parts[0].strip()
                        end_timestamp = parse_timestamp(end_timestamp_str)

                        if current_raid.get('map') != 'Unknown':
                            # 이전 레이드 종료
                            current_raid['raid_end'] = end_timestamp_str
                            current_raid['raid_end_dt'] = end_timestamp

                            if current_raid['raid_start_dt'] and end_timestamp:
                                duration = (end_timestamp - current_raid['raid_start_dt']).total_seconds() / 60
                                current_raid['duration_min'] = f"{duration:.1f}"

                            raid_configs.append(current_raid.copy())

                            # 새 레이드 시작
                            if '/client/raid/configuration' in line and '---> Request' in line:
                                current_raid = {
                                    'session_folder': session_folder,
                                    'session_start': session_start,
                                    'raid_start': end_timestamp_str,
                                    'raid_start_dt': end_timestamp,
                                    'map': 'Unknown',
                                    'mode': 'Unknown',
                                    'raid_end': None,
                                    'raid_end_dt': None,
                                    'duration_min': None,
                                    'exit_status': 'N/A'
                                }

            # 마지막 레이드 처리
            if current_raid and current_raid.get('map') != 'Unknown':
                raid_configs.append(current_raid)

    except Exception as e:
        print(f"Error reading {backend_log}: {e}")
        return None

    # PMC/Scav 모드 매칭 (시간 기준으로 가장 가까운 세션 모드 사용)
    for raid in raid_configs:
        closest_mode = 'Unknown'
        if raid['raid_start_dt']:
            min_diff = None
            for ts, mode in session_modes.items():
                ts_dt = parse_timestamp(ts)
                if ts_dt:
                    diff = abs((ts_dt - raid['raid_start_dt']).total_seconds())
                    if min_diff is None or diff < min_diff:
                        min_diff = diff
                        closest_mode = mode
        raid['mode'] = closest_mode

    return raid_configs

def main():
    """모든 세션 분석"""
    all_raids = []

    # 모든 로그 폴더 가져오기
    log_folders = sorted([d for d in os.listdir(LOG_BASE_PATH) if d.startswith('log_')])

    print(f"Found {len(log_folders)} log sessions")

    for i, folder in enumerate(log_folders):
        print(f"Analyzing {i+1}/{len(log_folders)}: {folder}")
        raids = analyze_session(folder)
        if raids:
            all_raids.extend(raids)

    # 결과 출력 (마크다운 테이블)
    print("\n" + "="*100)
    print("RAID ANALYSIS RESULTS")
    print("="*100 + "\n")

    print("| Session Folder | Raid Start | Map | Mode | Duration (min) | Exit Status |")
    print("|----------------|------------|-----|------|----------------|-------------|")

    for raid in all_raids:
        print(f"| {raid['session_folder']} | {raid['raid_start'][:19]} | {raid['map']} | {raid['mode']} | {raid['duration_min'] or 'N/A'} | {raid['exit_status']} |")

    print(f"\nTotal raids found: {len(all_raids)}")

if __name__ == "__main__":
    main()
