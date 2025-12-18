import os
from pathlib import Path
from datetime import datetime
import re

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

def analyze_session(session_folder):
    """세션에서 레이드 정보 추출"""
    session_path = Path(LOG_BASE_PATH) / session_folder

    # network-connection 로그 확인
    network_log = None
    for file in session_path.glob('*network-connection*.log'):
        network_log = file
        break

    if not network_log or not network_log.exists():
        return None  # 레이드 없음

    # application 로그에서 세션 모드 찾기
    application_log = None
    for file in session_path.glob('*application*.log'):
        application_log = file
        break

    raid_mode = 'Unknown'
    game_started_times = []

    if application_log and application_log.exists():
        try:
            with open(application_log, 'r', encoding='utf-8', errors='ignore') as f:
                for line in f:
                    if 'Session mode:' in line:
                        parts = line.split('|')
                        if len(parts) >= 5:
                            raid_mode = 'PVE' if 'Pve' in line else 'PVP'

                    # GameStarted 이벤트 찾기
                    if 'GameStarted:' in line:
                        parts = line.split('|')
                        if len(parts) >= 5:
                            timestamp_str = parts[0].strip()
                            game_started_times.append(timestamp_str)
        except Exception as e:
            print(f"Error reading application log for {session_folder}: {e}")

    # network-connection 로그에서 연결 정보 찾기
    raids = []
    try:
        with open(network_log, 'r', encoding='utf-8', errors='ignore') as f:
            lines = f.readlines()

            raid_start = None
            raid_start_dt = None
            raid_end = None
            raid_end_dt = None
            server_address = None

            for line in lines:
                if 'Enter to the \'Connected\' state' in line:
                    parts = line.split('|')
                    if len(parts) >= 5:
                        timestamp_str = parts[0].strip()
                        raid_start = timestamp_str
                        raid_start_dt = parse_timestamp(timestamp_str)

                        # address 추출
                        match = re.search(r'address: ([0-9.]+:[0-9]+)', line)
                        if match:
                            server_address = match.group(1)

                elif 'Enter to the \'Disconnected\' state' in line:
                    parts = line.split('|')
                    if len(parts) >= 5:
                        timestamp_str = parts[0].strip()
                        raid_end = timestamp_str
                        raid_end_dt = parse_timestamp(timestamp_str)

                        if raid_start and raid_start_dt and raid_end_dt:
                            duration = (raid_end_dt - raid_start_dt).total_seconds() / 60

                            raids.append({
                                'session_folder': session_folder,
                                'raid_start': raid_start,
                                'raid_end': raid_end,
                                'duration_min': f"{duration:.1f}",
                                'mode': raid_mode,
                                'map': 'Unknown',
                                'server': server_address
                            })

                        # 다음 레이드를 위해 리셋
                        raid_start = None
                        raid_start_dt = None
                        raid_end = None
                        raid_end_dt = None
                        server_address = None

    except Exception as e:
        print(f"Error reading network log for {session_folder}: {e}")

    return raids

def main():
    """모든 세션 분석"""
    all_raids = []

    # 모든 로그 폴더 가져오기
    log_folders = sorted([d for d in os.listdir(LOG_BASE_PATH) if d.startswith('log_')])

    print(f"Found {len(log_folders)} log sessions")
    print("Analyzing...\n")

    for i, folder in enumerate(log_folders):
        raids = analyze_session(folder)
        if raids:
            all_raids.extend(raids)
            print(f"  [{i+1}/{len(log_folders)}] {folder}: {len(raids)} raid(s)")

    # 결과 출력
    print("\n" + "="*100)
    print("RAID ANALYSIS RESULTS")
    print("="*100 + "\n")

    print(f"Total sessions analyzed: {len(log_folders)}")
    print(f"Sessions with raids: {len([raids for raids in [analyze_session(f) for f in log_folders] if raids])}")
    print(f"Total raids found: {len(all_raids)}\n")

    if all_raids:
        print("| # | Session Folder | Raid Start | Raid End | Duration (min) | Mode | Map | Server |")
        print("|---|----------------|------------|----------|----------------|------|-----|--------|")

        for i, raid in enumerate(all_raids, 1):
            print(f"| {i} | {raid['session_folder']} | {raid['raid_start'][:19]} | {raid['raid_end'][:19]} | {raid['duration_min']} | {raid['mode']} | {raid['map']} | {raid['server'] or 'N/A'} |")
    else:
        print("No raids found in any session.")

if __name__ == "__main__":
    main()
