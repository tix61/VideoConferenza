echo "# Verifica lo stato"
git status

echo "# Aggiungere TUTTI i file modificati:"
git add .

echo "# Prepara il commit"
git commit -m "Modifiche del $(date +'%d/%m/%Y alle %H:%M:%S')"

echo "# modifiche su gitHub"
git push origin main
