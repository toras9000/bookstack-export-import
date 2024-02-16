#!/usr/bin/with-contenv bash

# Copy the theme template at first startup.
if [ -d /assets/template/themes/my-theme ] && [ ! -e /config/www/themes/my-theme ]; then
    echo Copy theme template
    mkdir -p /config/www/themes
    cp -RT /assets/template/themes/my-theme    /config/www/themes/my-theme
fi

# Override environment settings
override_envs=(
    APP_THEME
    API_REQUESTS_PER_MIN
    MAIL_HOST
    MAIL_PORT
    MAIL_USERNAME
    MAIL_PASSWORD
    MAIL_ENCRYPTION
    MAIL_VERIFY_SSL
)

for env_name in "${override_envs[@]}"; do
    # Check if variables for customization of environments are defined.
    custom_env_val=$(eval echo \${CUSTOM_${env_name}})
    if [ -n "${custom_env_val}" ]; then
        # Check for definitions in .env
        if [ -z "$(grep -e "^\s*${env_name}\s*=" /config/www/.env)" ]; then
            # If it does not exist, add it.
            echo "Add env '${env_name}'"
            echo ""                          >> /config/www/.env
            echo "${env_name}=${custom_env_val}"  >> /config/www/.env
        else
            # Update if exist.
            echo "Replace env '${env_name}'"
            sed -i -E "s#^${env_name}=.*#${env_name}=${custom_env_val}#" /config/www/.env
        fi
    fi
done

# Create test API token
echo Create test API token
php /app/www/artisan bookstack:test-api-token
