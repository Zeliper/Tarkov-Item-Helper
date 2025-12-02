#!/usr/bin/env python3
"""
Extract quest list from Wiki HTML file and output as JSON grouped by Trader.
"""

import json
import re
from html.parser import HTMLParser
from pathlib import Path

HTML_FILE = Path(r"TarkovHelper\bin\Debug\net8.0-windows\Cache\WikiQuestPage.html")
OUTPUT_FILE = Path(r"TarkovHelper\bin\Debug\net8.0-windows\Cache\quests_by_trader.json")

# Trader order matching the tab order in the Wiki page
TRADERS = [
    "Prapor",
    "Therapist",
    "Fence",
    "Skier",
    "Peacekeeper",
    "Mechanic",
    "Ragman",
    "Jaeger",
    "Ref",
    "Lightkeeper",
    "BTR Driver"
]

def extract_quests_from_html(html_content: str) -> dict:
    """Parse HTML and extract quests grouped by trader."""

    result = {}

    # Find each table (tpt-1 to tpt-11)
    for i, trader in enumerate(TRADERS, start=1):
        table_id = f"tpt-{i}"
        result[trader] = []

        # Find table content - look for the table with this ID
        table_pattern = rf'<table id="{table_id}"[^>]*>(.*?)</table>'
        table_match = re.search(table_pattern, html_content, re.DOTALL)

        if not table_match:
            print(f"Warning: Table {table_id} not found for {trader}")
            continue

        table_content = table_match.group(1)

        # Extract quest names from data-tpt-row-id attributes
        quest_pattern = r'data-tpt-row-id="([^"]+)"'
        quests = re.findall(quest_pattern, table_content)

        # Remove duplicates while preserving order
        seen = set()
        unique_quests = []
        for quest in quests:
            if quest not in seen:
                seen.add(quest)
                unique_quests.append(quest)

        # Also extract wiki links for validation and additional info
        link_pattern = r'<td><a href="/wiki/([^"]+)"[^>]*>([^<]+)</a>'
        links = re.findall(link_pattern, table_content)

        for quest_name in unique_quests:
            # Find matching link info
            wiki_path = quest_name.replace(" ", "_")
            quest_data = {
                "name": quest_name,
                "wikiPath": f"/wiki/{wiki_path}"
            }
            result[trader].append(quest_data)

        print(f"{trader}: {len(unique_quests)} quests found")

    return result

def main():
    if not HTML_FILE.exists():
        print(f"Error: HTML file not found: {HTML_FILE}")
        return

    print(f"Reading: {HTML_FILE}")
    html_content = HTML_FILE.read_text(encoding="utf-8")

    print(f"HTML size: {len(html_content):,} characters")

    quests_by_trader = extract_quests_from_html(html_content)

    # Calculate totals
    total = sum(len(quests) for quests in quests_by_trader.values())
    print(f"\nTotal quests: {total}")

    # Save to JSON
    OUTPUT_FILE.parent.mkdir(parents=True, exist_ok=True)
    with open(OUTPUT_FILE, "w", encoding="utf-8") as f:
        json.dump(quests_by_trader, f, indent=2, ensure_ascii=False)

    print(f"\nSaved to: {OUTPUT_FILE}")

if __name__ == "__main__":
    main()
