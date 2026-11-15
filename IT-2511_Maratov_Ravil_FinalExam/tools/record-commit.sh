#!/usr/bin/env sh
# Writes the identifier of the current commit (the submitted code) into README.md and GIT_REPOSITORY.txt,
# commits that change and pushes it. A commit cannot contain its own identifier, so the recorded id is the
# commit of the code; the new commit changes only these two text files.
# Run in Git Bash, from the repository folder, AFTER the final project commit was pushed to branch main.
set -e
cd "$(dirname "$0")/.."
ID=$(git rev-parse HEAD)
sed "s|COMMIT_ID_PLACEHOLDER|$ID|" README.md > README.tmp && mv README.tmp README.md
printf 'Repository: https://github.com/Calakula/delivery-quotes-fp\nBranch: main\nFinal commit identifier of the submitted code: %s\n' "$ID" > GIT_REPOSITORY.txt
git add README.md GIT_REPOSITORY.txt
git commit -m "Record the submitted commit id"
git push origin main
echo "Submitted code commit: $ID"
