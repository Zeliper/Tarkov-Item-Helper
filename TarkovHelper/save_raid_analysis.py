import os
import re
from pathlib import Path
from datetime import datetime

LOG_BASE_PATH = r"C:\Program Files (x86)\Steam\steamapps\common\Escape from Tarkov\build\Logs"
OUTPUT_FILE = r"C:\Users\Zeliper\source\repos\TarkovHelper\TarkovHelper\eft_raid_analysis.md"

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

def extract_map_info(application_log_path, raid_start_time):
    """application 로그에서 맵 정보 추출"""
    raid_start_dt = parse_timestamp(raid_start_time)
    if not raid_start_dt:
        return None

    map_name = None
    try:
        with open(application_log_path, 'r', encoding='utf-8', errors='ignore') as f:
            closest_time_diff = None

            for line in f:
                if 'Location:' in line:
                    parts = line.split('|')
                    if len(parts) >= 5:
                        timestamp_str = parts[0].strip()
                        timestamp = parse_timestamp(timestamp_str)

                        if timestamp:
                            time_diff = abs((timestamp - raid_start_dt).total_seconds())

                            if time_diff < 300:
                                if closest_time_diff is None or time_diff < closest_time_diff:
                                    closest_time_diff = time_diff

                                    match = re.search(r'Location:\s*([^\s,]+)', line)
                                    if match:
                                        location_id = match.group(1)
                                        map_name = MAP_NAMES.get(location_id, location_id)

    except Exception as e:
        pass

    return map_name

def analyze_session(session_folder):
    """세션에서 레이드 정보 추출"""
    session_path = Path(LOG_BASE_PATH) / session_folder

    network_log = None
    for file in session_path.glob('*network-connection*.log'):
        network_log = file
        break

    if not network_log or not network_log.exists():
        return None

    application_log = None
    for file in session_path.glob('*application*.log'):
        application_log = file
        break

    raid_mode = 'Unknown'

    if application_log and application_log.exists():
        try:
            with open(application_log, 'r', encoding='utf-8', errors='ignore') as f:
                for line in f:
                    if 'Session mode:' in line:
                        raid_mode = 'PVE' if 'Pve' in line else 'PVP'
                        break
        except:
            pass

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

                            map_name = 'Unknown'
                            if application_log:
                                extracted_map = extract_map_info(application_log, raid_start)
                                if extracted_map:
                                    map_name = extracted_map

                            raids.append({
                                'session_folder': session_folder,
                                'raid_start': raid_start,
                                'raid_end': raid_end,
                                'duration_min': f"{duration:.1f}",
                                'mode': raid_mode,
                                'map': map_name,
                                'server': server_address
                            })

                        raid_start = None
                        raid_start_dt = None
                        raid_end = None
                        raid_end_dt = None
                        server_address = None

    except Exception as e:
        pass

    return raids

def main():
    """모든 세션 분석하고 결과를 파일로 저장"""
    all_raids = []

    log_folders = sorted([d for d in os.listdir(LOG_BASE_PATH) if d.startswith('log_')])

    print(f"Analyzing {len(log_folders)} sessions...")

    for i, folder in enumerate(log_folders):
        print(f"  [{i+1}/{len(log_folders)}] {folder}", end='\r')
        raids = analyze_session(folder)
        if raids:
            all_raids.extend(raids)

    print(f"\nTotal raids found: {len(all_raids)}")
    print(f"Saving to: {OUTPUT_FILE}")

    # 마크다운 파일로 저장
    with open(OUTPUT_FILE, 'w', encoding='utf-8') as f:
        f.write("# Escape from Tarkov - Raid Analysis\n\n")
        f.write(f"**Analysis Date:** {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n\n")

        f.write("## Summary\n\n")
        f.write(f"- **Total Sessions Analyzed:** {len(log_folders)}\n")
        f.write(f"- **Sessions with Raids:** {len([r for r in [analyze_session(folder) for folder in log_folders] if r])}\n")
        f.write(f"- **Total Raids Found:** {len(all_raids)}\n\n")

        # 맵별 통계
        map_stats = {}
        for raid in all_raids:
            map_name = raid['map']
            if map_name not in map_stats:
                map_stats[map_name] = 0
            map_stats[map_name] += 1

        f.write("## Map Statistics\n\n")
        f.write("| Map | Raids | Percentage |\n")
        f.write("|-----|-------|------------|\n")
        for map_name, count in sorted(map_stats.items(), key=lambda x: x[1], reverse=True):
            percentage = (count / len(all_raids)) * 100
            f.write(f"| {map_name} | {count} | {percentage:.1f}% |\n")

        # 일별 통계
        daily_stats = {}
        for raid in all_raids:
            date = raid['raid_start'][:10]
            if date not in daily_stats:
                daily_stats[date] = {'count': 0, 'total_duration': 0}
            daily_stats[date]['count'] += 1
            daily_stats[date]['total_duration'] += float(raid['duration_min'])

        f.write("\n## Daily Statistics\n\n")
        f.write("| Date | Raids | Total Time (hours) | Avg Duration (min) |\n")
        f.write("|------|-------|--------------------|--------------------|")
        for date, stats in sorted(daily_stats.items()):
            total_hours = stats['total_duration'] / 60
            avg_duration = stats['total_duration'] / stats['count']
            f.write(f"\n| {date} | {stats['count']} | {total_hours:.1f} | {avg_duration:.1f} |")

        # 상세 레이드 목록
        f.write("\n\n## Detailed Raid List\n\n")
        f.write("| # | Date | Start | End | Duration | Mode | Map | Server |\n")
        f.write("|---|------|-------|-----|----------|------|-----|--------|")

        for i, raid in enumerate(all_raids, 1):
            date = raid['raid_start'][:10]
            start_time = raid['raid_start'][11:19]
            end_time = raid['raid_end'][11:19]
            server_ip = raid['server'].split(':')[0] if raid['server'] else 'N/A'
            f.write(f"\n| {i} | {date} | {start_time} | {end_time} | {raid['duration_min']} min | {raid['mode']} | {raid['map']} | {server_ip} |")

    print(f"Analysis saved successfully!")

if __name__ == "__main__":
    main()
