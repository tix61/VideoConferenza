echo "# Verifica lo stato"
git status

echo "# Aggiungere TUTTI i file modificati:"
git add .

echo "# Prepara il commit"
set "datetime=%DATE% %TIME%"
git commit -m "Modifiche del %datetime%"

echo "# modifiche su gitHub"
git push origin main
