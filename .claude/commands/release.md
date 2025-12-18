# Release Command

버전 $ARGUMENTS 으로 릴리스를 수행합니다.

## 수행할 작업

1. **update.xml 버전 업데이트**: `<version>` 태그와 다운로드 URL의 버전을 $ARGUMENTS 로 변경
2. **TarkovHelper.csproj 버전 업데이트**: `<Version>`, `<AssemblyVersion>`, `<FileVersion>` 모두 $ARGUMENTS 로 변경
3. **Release 빌드**: `dotnet build TarkovHelper\TarkovHelper.csproj -c Release`
4. **ZIP 파일 생성**: `TarkovHelper\bin\Release\CreateRelease.bat` 실행
5. **Git 커밋 및 태그**:
   - `git add -A`
   - `git commit -m "v$ARGUMENTS Release"`
   - `git tag v$ARGUMENTS`
6. **Push**: `git push origin main && git push origin v$ARGUMENTS`
7. **GitHub Release 생성**: `gh release create v$ARGUMENTS --title "v$ARGUMENTS" --notes "Release notes" TarkovHelper.zip`

## 주의사항

- gh CLI가 설치되어 있고 인증되어 있어야 합니다 (`gh auth login`)
- 빌드 실패 시 중단합니다
- Release 노트는 간단하게 생성되며, 상세 내용은 GitHub 웹에서 수정 가능합니다

위 작업들을 순서대로 실행해주세요. 각 단계마다 결과를 확인하고 진행하세요.
