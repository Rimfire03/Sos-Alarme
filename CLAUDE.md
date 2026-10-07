# Git et release

- Une release se fait toute seule : chaque push sur `main` lance la CI (`.github/workflows/release.yml`) qui
  compile, signe (Windows + macOS) et publie. Le hook `.githooks/pre-commit` incrémente la version à chaque commit
  (vérifier `git config core.hooksPath` = `.githooks`). **« Faire une release » = commit + push**, rien d'autre à lancer
  en local : `git-commit.ps1 -Push` (voir les consignes git globales).
- Ne pas pousser un commit qui ne doit pas déclencher de release publique.
