#!/usr/bin/env bash
# Creates the generated accounts of the migration source: user, home with content, database, crontab, virtual host.
set -Eeuo pipefail

accounts="${ACCOUNTS:-10}"

mysqld_safe --skip-networking >/dev/null 2>&1 &
until mysqladmin ping --silent >/dev/null 2>&1; do sleep 1; done

for index in $(seq 1 "${accounts}"); do
  name="$(printf 'site%02d' "${index}")"
  useradd --create-home --shell /usr/sbin/nologin "${name}"
  mkdir -p "/home/${name}/public_html"
  printf '<html><head><title>%s source</title></head><body>%s</body></html>\n' "${name}" "${name}" \
    > "/home/${name}/public_html/index.html"
  # A file the final pass has to delete after the initial one copied it (the delta at cut-over).
  printf 'removed-before-cut-over\n' > "/home/${name}/public_html/stale.txt"
  chown -R "${name}:${name}" "/home/${name}"

  mysql -e "CREATE DATABASE \`${name}_shop\`; CREATE TABLE \`${name}_shop\`.items (id INT PRIMARY KEY, label VARCHAR(40)); INSERT INTO \`${name}_shop\`.items VALUES (1, '${name}');"

  printf '0 3 * * * /bin/true\n' | crontab -u "${name}" -

  cat > "/etc/nginx/conf.d/${name}.conf" <<NGINX
server {
    listen 80;
    server_name ${name}.source.test;
    root /home/${name}/public_html;
}
NGINX
done

mysqladmin shutdown
