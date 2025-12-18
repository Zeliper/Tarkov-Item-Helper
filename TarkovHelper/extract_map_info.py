import os
import re
from pathlib import Path
from datetime import datetime

LOG_BASE_PATH = r"C:\Program Files (x86)\Steam\steamapps\common\Escape from Tarkov\build\Logs"

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
        try:
            return datetime.strptime(timestamp_str, '%Y-%m-%d %H:%M:%S')
        except:
            return None

def extract_map_from_session(session_folder, raid_start_time):
    """세션에서 특정 레이드의 맵 정보 추출"""
    session_path = Path(LOG_BASE_PATH) / session_folder

    # application 로그에서 맵 정보 찾기
    application_log = None
    for file in session_path.glob('*application*.log'):
        application_log = file
        break

    if not application_log or not application_log.exists():
        return None

    raid_start_dt = parse_timestamp(raid_start_time)
    if not raid_start_dt:
        return None

    # application 로그에서 GameStarted 근처의 location 정보 찾기
    map_name = None
    try:
        with open(application_log, 'r', encoding='utf-8', errors='ignore') as f:
            # 레이드 시작 시간과 가장 가까운 location 정보 찾기
            closest_time_diff = None

            for line in f:
                # Location 정보가 포함된 라인 찾기
                if 'Location:' in line:
                    parts = line.split('|')
                    if len(parts) >= 5:
                        timestamp_str = parts[0].strip()
                        timestamp = parse_timestamp(timestamp_str)

                        if timestamp:
                            time_diff = abs((timestamp - raid_start_dt).total_seconds())

                            # 레이드 시작 전후 5분 이내의 location 정보만 고려
                            if time_diff < 300:
                                if closest_time_diff is None or time_diff < closest_time_diff:
                                    closest_time_diff = time_diff

                                    # Location 이름 추출
                                    match = re.search(r'Location:\s*([^\s,]+)', line)
                                    if match:
                                        location_id = match.group(1)
                                        map_name = MAP_NAMES.get(location_id, location_id)

    except Exception as e:
        print(f"Error reading application log for {session_folder}: {e}")

    return map_name

def main():
    """이전에 추출한 레이드 정보 읽고 맵 정보 추가"""
    # 테스트용으로 한 세션만 분석
    test_session = "log_2025.12.17_21-05-02_1.0.0.5.42334"
    raid_start = "2025-12-17 22:04:31.959"

    print(f"Testing map extraction for session: {test_session}")
    print(f"Raid start time: {raid_start}")

    map_name = extract_map_from_session(test_session, raid_start)

    if map_name:
        print(f"Map found: {map_name}")
    else:
        print("Map not found")

        # application 로그를 직접 읽어서 패턴 확인
        session_path = Path(LOG_BASE_PATH) / test_session
        application_log = None
        for file in session_path.glob('*application*.log'):
            application_log = file
            break

        if application_log:
            print("\nSearching for location-related patterns...")
            try:
                with open(application_log, 'r', encoding='utf-8', errors='ignore') as f:
                    for i, line in enumerate(f):
                        if 'location' in line.lower() or 'map' in line.lower():
                            if '2025-12-17 22:' in line[:19]:  # 시간대 필터
                                print(f"Line {i+1}: {line.strip()[:200]}")
            except Exception as e:
                print(f"Error: {e}")

if __name__ == "__main__":
    main()
