#!/bin/bash
clear
echo " ██████╗ █████╗ ██╗     ██╗         ██╗     ██╗███╗   ███╗██╗████████╗███████╗██████╗ ";
echo "██╔════╝██╔══██╗██║     ██║         ██║     ██║████╗ ████║██║╚══██╔══╝██╔════╝██╔══██╗";
echo "██║     ███████║██║     ██║         ██║     ██║██╔████╔██║██║   ██║   █████╗  ██████╔╝";
echo "██║     ██╔══██║██║     ██║         ██║     ██║██║╚██╔╝██║██║   ██║   ██╔══╝  ██╔══██╗";
echo "╚██████╗██║  ██║███████╗███████╗    ███████╗██║██║ ╚═╝ ██║██║   ██║   ███████╗██║  ██║";
echo " ╚═════╝╚═╝  ╚═╝╚══════╝╚══════╝    ╚══════╝╚═╝╚═╝     ╚═╝╚═╝   ╚═╝   ╚══════╝╚═╝  ╚═╝";
echo "";
echo "Ver 1.0.0"

# ------------------------------ CLM files path ------------------------------ #
CLM_SERVER_URL="https://github.com/tishige/CallLimiter/releases/download/v1.0.0/CallLimiter.zip"
CLM_WEB_URL="https://github.com/tishige/CallLimiter/releases/download/v1.0.0/CallLimiterWeb.zip"

# ---------------------------------- Logging --------------------------------- #
DEFAULT='\033[0m'
WHITE='\033[0;02m'
GREEN='\033[1;32m'
YELLOW='\033[1;33m'
RED='\033[1;31m'
CYAN='\033[1;36m'
RED_BG='\033[41m'

logDebug() {
    echo -e "${DEFAULT}${1}${DEFAULT}"
}

logInfo() {
    echo -e "${CYAN}${1}${DEFAULT}"
}

logSuccess() {
    echo -e "${GREEN}${1}${DEFAULT}"
}

logError() {
    echo -e "${RED}${1}${DEFAULT}"
}

logErrorWhite() {    
    echo -e "${WHITE}${RED_BG}${1}${DEFAULT}"
}   


logWarn() {
    echo -e "${YELLOW}${1}${DEFAULT}"
}

Prompt() {
    local oldIFS=$IFS
    IFS=$'\n'
    local promptLines=($1)
    for index in "${!promptLines[@]}"; do
        if (( index == ${#promptLines[@]}-1 )); then
            echo -n -e "${YELLOW}${promptLines[$index]}${DEFAULT}"
        else
            echo -e "${YELLOW}${promptLines[$index]}${DEFAULT}"
        fi
    done
    IFS=$oldIFS
}

result_write() {
    echo "$@" >> $(getent passwd $SUDO_USER | cut -d: -f6)/CLM_Install_Result.txt
}

########################################################################################################################
#
# Check deployment model
#
########################################################################################################################
logInfo "Checking OS..."
ubuntu=$(lsb_release -cs)
if [ "$ubuntu" != "jammy" ]; then
    logError "CallLimiter can only be installed on Ubuntu 22.04 LTS."
    exit 1
    else
    logSuccess "Ubuntu 22.04 LTS OK"
fi

logInfo "Checking deployment model..."
localIP=$(hostname -I | awk '{print $1}')

#AWS or Azure or GCP or not?
publicIP=$(curl -s --connect-timeout 5 -H Metadata:true "http://169.254.169.254/latest/meta-data/public-ipv4")
if [[ $publicIP =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    logSuccess "Running on AWS. Elastic IP address is $publicIP"
else
    # Azure?
    response=$(curl -s --connect-timeout 5 -H Metadata:true "http://169.254.169.254/metadata/instance?api-version=2021-05-01")  
    if echo "$response" | grep -q "azEnvironment"; then
        publicIP=$(curl -4 http://l2.io/ip)
        logSuccess "Running on Azure. Public IP address is $publicIP"
    else    
        # GCP?  
        response=$(curl -s --connect-timeout 5 -H "Metadata-Flavor: Google" "http://169.254.169.254/computeMetadata/v1/instance/network-interfaces/0/ip")  
        if [[ $response =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
            publicIP=$(curl -4 http://l2.io/ip)
            logSuccess "Running on GCP. Public IP address is $publicIP"
        else  
            logSuccess "Could not detect an assigned public IP address."
        fi
    fi
fi

########################################################################################################################
#
# Collecting User Input
#
########################################################################################################################
while true; do  
logWarn "Choose the type of IP address to use for SIP communication."
printf "\033[1;91m[\033[0m1\033[1;91m]\033[1;33m Use a public IP address to receive inbound calls from SIP carrier. \n\033[0m"    
printf "\033[1;91m[\033[0m2\033[1;91m]\033[1;33m Use a local IP address to receive inbound calls from On-premise voice gateway or SBC.\n\033[0m"  
echo -e -n "\e[1;33m Choose Option:\e[0m"

    read ipChoice

    case $ipChoice in
        1)
            usepublicIP=true
            break
            ;;
        2)
            usepublicIP=false
            break
            ;;
        *)  
            logError "Invalid input, please try again."
            ;;
    esac
done

if $usepublicIP; then
    while true; do
        if [ -z "$publicIP" ]; then
            while true; do
                Prompt "Please enter a public IP address of this server:"
                read publicIP
                if [ -n "$publicIP" ]; then
                    break
                else
                    logError "Input is empty, please enter again."
                fi
            done
        fi
        Prompt "Do you use this public IP address $publicIP ? (y/N):"
        read answer
        if [ "$answer" == "y" ]; then
            break
        else
            publicIP=""
        fi
    done
fi

Prompt "Do you want to change time zone settings? (y/N):"  
read changeTimezone  
if [[ $changeTimezone = "y" || $changeTimezone = "Y" ]]; then  
    sudo dpkg-reconfigure tzdata  
else  
    logInfo "No changes were made to the time zone settings."
fi  


logInfo "SIP protocol configuration"
# Use the global IP if available, otherwise use the local IP
if $usepublicIP; then
    defaultIP=$publicIP
else
    defaultIP=$localIP
fi

while true
do
    DEFAULT_DOMAIN_NAME=$defaultIP
    Prompt "Please enter the FQDN or IP Address of this server (e.g., calllimit.example.com) default $defaultIP:"
    read DOMAIN_NAME

    if [ -z "$DOMAIN_NAME" ]; then
        DOMAIN_NAME=$DEFAULT_DOMAIN_NAME
    fi

    logDebug "You entered the domain name: $DOMAIN_NAME"
    Prompt "Is this correct? (y/N):"
    read confirmation
    if [ "$confirmation" = "y" -o "$confirmation" = "Y" ]
    then
        break
    fi
done

# --------------------------- Decide SIP trunk port -------------------------- #
logInfo "SIP protocol configuration"
while true; do
    logWarn "Choose the trunk transport protocol that you want to use for the voice gateway or the SIP carrier."
    printf "\033[1;91m[\033[0m1\033[1;91m]\033[1;33m UDP \n\033[0m"
    printf "\033[1;91m[\033[0m2\033[1;91m]\033[1;33m TCP \n\033[0m"
    printf "\033[1;91m[\033[0m3\033[1;91m]\033[1;33m TLS\n\033[0m"
    echo -e -n "\e[1;33m Choose Option:\e[0m"  
    read protocol

    case $protocol in  
        1)   
            Prompt "Enter the UDP port number (Press ENTER for default 5060):"
            read port
            if [ -z "$port" ]
            then
                port=5060
            fi
            logDebug "Port number set to $port"
            break
            ;;
        2)
            Prompt "Enter the TCP port number (Press ENTER for default 5060):"
            read port
            if [ -z "$port" ]
            then
                port=5060
            fi
            logDebug "Port number set to $port"
            break  
            ;;
        3)
            Prompt "Enter the TLS port number (Press ENTER for default 5061):"
            read port
            if [ -z "$port" ]
            then
                port=5061
            fi
            logDebug "Port number set to $port"
            break  
            ;;
        *)
            logError "Invalid input, please try again."
            ;;
    esac
done

logInfo "Genesys Cloud authenticate configuration"
while true; do
    while true; do
        Prompt "Please enter your Environment(e.g., mypurecloud.com):"
        read environment
        if [ -n "$environment" ]; then
            break
        else
            logError "Input is empty, please enter again."
        fi
    done
    logDebug "Genesys Cloud Enviroment: $environment"
    Prompt "Is this correct? (y/N):"
    read answer
    if [ "$answer" == "y" ]; then
        break
    fi
done

while true; do
    while true; do
        Prompt "Please enter your Genesys Cloud Organization name(e.g., avaya):"
        read orgname
        if [ -n "$orgname" ]; then
            break
        else
            logError "Input is empty, please enter again."
        fi
    done
    logDebug "Your Genesys Cloud Organization name: $orgname"
    Prompt "Is this correct? (y/N):"
    read answer
    if [ "$answer" == "y" ]; then
        break
    fi
done

logInfo "Please enter Client Credentials to receive queues statistics from Genesys Cloud."
while true; do
    while true; do
        Prompt "Please enter CLIENT CREDENTIALS's ClientId:"
        read clientId
        if [ -n "$clientId" ]; then
            break
        else
            logError "ClientId is empty, please enter again."
        fi  
    done
    while true; do
        Prompt "Please enter CLIENT CREDENTIALS's ClientSecret:"
        read clientSecret
        if [ -n "$clientSecret" ]; then
            break
        else
            logError "ClientSecret is empty, please enter again."
        fi
    done
    logInfo "Entered Client Credentials"
    logDebug "ClientId: $clientId"
    logDebug "ClientSecret: $clientSecret"
    Prompt "Is this correct? (y/N):"
    read answer
    if [ "$answer" == "y" ]; then
        break
    fi
done


logInfo "Please enter Code Authorization to log into the CallLimiter using your GenesysCloud account."
while true; do
    while true; do
        Prompt "Please enter CODE AUTHORIZATION's ClientId:"
        read clientIdCA
        if [ -n "$clientIdCA" ]; then
            break
        else
            logError "ClientId is empty, please enter again."
        fi
    done
    while true; do
        Prompt "Please enter CODE AUTHORIZATION's ClientSecret:"
        read clientSecretCA
        if [ -n "$clientSecretCA" ]; then
            break
        else
            logError "ClientSecret is empty, please enter again."
        fi
    done
    logInfo "Entered Code Authorization"
    logDebug "ClientId: $clientIdCA"
    logDebug "ClientSecret: $clientSecretCA"
    Prompt "Is this correct? (y/N):"
    read answer
    if [ "$answer" == "y" ]; then
        break
    fi
done

while true; do
    redirectURL="https://$DOMAIN_NAME/GCLogin/Index"
    logInfo "The address to redirect from Genesys Cloud to this server is $redirectURL"
    Prompt "Is this correct? (y/N):"
    read answer
    if [ "$answer" == "y" ]; then
        break
    else
        logInfo "Please enter redirect URI to log into the CallLimiter."
        while true; do
            Prompt "Redirect URL (e.g., https://callLimiter.example.com/GCLogin/Index):"
            read redirectURL

            if [ -z "$redirectURL" ]; then
                logError "Redirect URL is empty, please enter again."
            elif [[ "$redirectURL" != */GCLogin/Index ]]; then
                logError "The Redirect URL must end with /GCLogin/Index."
                continue
            else
                logDebug "Redirect URL:$redirectURL"
                Prompt "Is this correct? (y/N):"
                read answer
                if [ "$answer" == "y" ]; then
                    break 2
                fi
            fi
        done
    fi
done

declare -a lines
declare -a comments

logInfo "Genesys Cloud configuration"
while true
do
    if $usepublicIP; then
        logWarn "Please enter the domain part of your SIP external trunk URI in the 'Inbound Request-URI Reference' field."
    else
        logWarn "Please enter the On premise EDGE IP address and port."
    fi

    lines=()
    comments=()

    while true
    do
        if $usepublicIP; then
            while true
            do
                Prompt "Domain part (e.g., myuniqueidentifier.byoc.mypurecloud.com or ENTER to exit):"
                read line
                # Check if line contains port number
                if [[ "$line" =~ :[0-9]+$ ]]; then
                    logError "Do not set port number.Please try again."
                else
                    break
                fi
            done
        else
            Prompt "EDGE IP address and port (e.g., 172.20.99.88:5060 or ENTER to exit):"
            read line
        fi
        if [ -z "$line" ]
        then
            break
        fi

        while true
        do
            Prompt "Please enter a comment (e.g., EDGE1):"
            read comment
            # Check if comment contains invalid characters
            if [[ "$comment" =~ [^a-zA-Z0-9_.\ ] ]]; then
                logWarn "Invalid characters detected. Please use only alphanumeric characters, underscores, periods, or spaces."
            else
                break
            fi
        done
        lines+=("$line")
        comments+=("$comment")
    done

    logInfo "You entered the following data:"
    for i in "${!lines[@]}"
    do
        logDebug "Domain part: ${lines[i]} | Comment: ${comments[i]}"
    done

    Prompt "Is this correct? (y/N):"
    read confirmation
    if [ "$confirmation" = "y" -o "$confirmation" = "Y" ]
    then
        break
    fi
done

echo ""
Prompt "The installation will begin. Do you want to proceed? (y/N):"
read proceedInstall
if [[ $proceedInstall = "n" || $proceedInstall = "N" ]]; then
    logError "Installation is being terminated."
    exit 1
fi

echo ""
logInfo "Processing..."
echo ""

########################################################################################################################
#
# Install requried applications
#
########################################################################################################################

# --------------------------- Set workingDirectory --------------------------- #
workingDirectory="CallLimiter"
username=$(sudo logname)
cd /home/$username
mkdir -p $workingDirectory
sudo chown -R $username:$username $workingDirectory
cd $workingDirectory

# ---------------------------- Disable needrestart --------------------------- #
sudo cat << 'EOF' > /etc/needrestart/conf.d/99_restart.conf
$nrconf{kernelhints} = '0';
$nrconf{restart} = 'a';
EOF
logSuccess "Disable needrestart done.ignore kernel updates."

# ---------------------------- Modify sources list --------------------------- #
logInfo "Adding sources list.This may take a while..."

set -e
sudo rm -f /usr/share/keyrings/redis-archive-keyring.gpg
curl -fsSL -m 30 https://packages.redis.io/gpg | sudo gpg --dearmor -o /usr/share/keyrings/redis-archive-keyring.gpg || { logError "Could not resolve host: packages.redis.io"; exit 1; }
sudo gpg --no-default-keyring --keyring /usr/share/keyrings/redis-archive-keyring.gpg --list-keys || { logError "gpg: no valid OpenPGP data found."; exit 1; }
echo "deb [signed-by=/usr/share/keyrings/redis-archive-keyring.gpg] https://packages.redis.io/deb $(lsb_release -cs) main" | sudo tee /etc/apt/sources.list.d/redis.list
logSuccess "Redis done"

sudo rm -f /usr/share/keyrings/nginx-archive-keyring.gpg
curl -fsSL -m 30 https://nginx.org/keys/nginx_signing.key | sudo gpg --dearmor -o /usr/share/keyrings/nginx-archive-keyring.gpg || { logError "Could not resolve host: nginx.org"; exit 1; }
sudo gpg --no-default-keyring --keyring /usr/share/keyrings/nginx-archive-keyring.gpg --list-keys || { logError "gpg: no valid OpenPGP data found."; exit 1; }
echo "deb [signed-by=/usr/share/keyrings/nginx-archive-keyring.gpg] http://nginx.org/packages/mainline/ubuntu $(lsb_release -cs) nginx" | sudo tee /etc/apt/sources.list.d/nginx.list
logSuccess "nginx done"

sudo rm -f /usr/share/keyrings/kamailio-archive-keyring.gpg
curl -fsSL -m 30 http://deb.kamailio.org/kamailiodebkey.gpg | sudo gpg --dearmor -o /usr/share/keyrings/kamailio-archive-keyring.gpg || { logError "Could not resolve host: deb.kamailio.org"; exit 1; }
sudo gpg --no-default-keyring --keyring /usr/share/keyrings/kamailio-archive-keyring.gpg --list-keys || { logError "gpg: no valid OpenPGP data found."; exit 1; }
echo "deb [signed-by=/usr/share/keyrings/kamailio-archive-keyring.gpg] http://deb.kamailio.org/kamailio $(lsb_release -cs) main" | sudo tee /etc/apt/sources.list.d/kamailio.list
sudo cp -f /usr/share/keyrings/kamailio-archive-keyring.gpg /etc/apt/trusted.gpg.d/ || { logError "Failed to copy keyring to /etc/apt/trusted.gpg.d/"; exit 1; }

# ------------------------ add the Kamailio repository ----------------------- #
echo "deb http://cz.archive.ubuntu.com/ubuntu jammy main" | sudo tee -a /etc/apt/sources.list.d/kamailio.list
echo "deb http://deb.kamailio.org/kamailio57 jammy main" | sudo tee -a /etc/apt/sources.list.d/kamailio.list
echo "deb-src http://deb.kamailio.org/kamailio57 jammy main" | sudo tee -a /etc/apt/sources.list.d/kamailio.list
logSuccess "kamailio done"

# ------------------------------- Update system ------------------------------ #
logInfo "Starting to update the package lists for upgrades and new package installations."
sudo apt-get update -y
sudo apt-get upgrade -y

# --------------------------- Download Applications -------------------------- #
logInfo "Install apps..."
apt-get install gnupg2 mariadb-server curl unzip ca-certificates lsb-release ubuntu-keyring kamailio* redis nginx -y expect

systemctl is-active --quiet CallLimiter && systemctl stop CallLimiter
systemctl is-active --quiet CallLimiterWeb && systemctl stop CallLimiterWeb

wget -N $CLM_SERVER_URL
mkdir -p /usr/local/CallLimiter
unzip -o CallLimiter.zip -d /usr/local/CallLimiter
chown -R $SUDO_USER:$SUDO_USER /usr/local/CallLimiter
chmod +x /usr/local/CallLimiter/CallLimiter
cp -f /usr/local/CallLimiter/CallLimiter.service /etc/systemd/system/
logSuccess "CallLimiter done"

wget -N $CLM_WEB_URL
mkdir -p /usr/local/CallLimiterWeb
unzip -o CallLimiterWeb.zip -d /usr/local/CallLimiterWeb
chown -R $SUDO_USER:$SUDO_USER /usr/local/CallLimiterWeb
chmod +x /usr/local/CallLimiterWeb/CallLimiterWeb

mkdir -p /var/log/CallLimiterWeb
chown www-data:www-data /var/log/CallLimiterWeb
chmod 755 /var/log/CallLimiterWeb

cp -f /usr/local/CallLimiterWeb/CallLimiterWeb.service /etc/systemd/system/
logSuccess "CallLimiterWeb done"

# ---------------------------- Verify installation --------------------------- #
# ----------------------------------- redis ---------------------------------- #
version_info=$(redis-server --version)
redis_version_number=$(echo "$version_info" | grep -oP 'Redis server v=\K[^ ]*(?= sha=)')
if [[ -z "$redis_version_number" ]]; then
    logError "redis installation failed."
    exit 1
else
    systemctl enable redis-server
    logSuccess "redis installation was successful. Version: $redis_version_number"
fi
# ----------------------------------- nginx ---------------------------------- #
version_info=$(nginx -version 2>&1)
nginx_version_number=$(echo "$version_info" | grep -oP 'nginx version: nginx/\K.*$')
if [[ -z "$nginx_version_number" ]]; then
    logError "nginx installation failed."
    exit 1
else
    logSuccess "nginx installation was successful. Version: $nginx_version_number"
fi

# --------------------------------- maria-db --------------------------------- #
version_info=$(mariadb --version)
mariadb_version_number=$(echo "$version_info" | grep -oP 'mariadb  Ver \K[^ ]*(?= Distrib)')
if [[ -z "$mariadb_version_number" ]]; then
    logError "mariadb installation failed."
    exit 1
else
    logSuccess "mariadb installation was successful. Version: $mariadb_version_number"
fi

logInfo "Setting mariadb root password as kamailiorw."
sudo mysql -u root <<EOF
ALTER USER 'root'@'localhost' IDENTIFIED BY 'kamailiorw';
EOF

# --------------------------------- kamailio --------------------------------- #
version_info=$(kamailio -V)
kamailio_version_number=$(echo "$version_info" | grep -oP 'version: kamailio \K.*(?= \(x86_64/linux\))')
if [[ -z "$kamailio_version_number" ]]; then
    logError "Kamailio installation failed."
    exit 1
else  
    logSuccess "Kamailio installation was successful. Version: $kamailio_version_number"
fi

# -------------------------------- CallLimiter ------------------------------- #
version_info=$(/usr/local/CallLimiter/CallLimiter --version)  
call_limiter_version_number=$(echo "$version_info" | grep -oP 'CallLimiter \K[^ ]*')
if [[ -z "$call_limiter_version_number" ]]; then
    logError "CallLimiter installation failed."
    exit 1
else  
    logSuccess "CallLimiter installation was successful. Version: $call_limiter_version_number"
fi

# ------------------------------ CallLimiterWeb ------------------------------ #
version_info=$(/usr/local/CallLimiterWeb/CallLimiterWeb --version)  
call_limiterweb_version_number=$(echo "$version_info" | grep -oP 'CallLimiterWeb \K[^ ]*')
if [[ -z "$call_limiterweb_version_number" ]]; then
    logError "CallLimiterWeb installation failed."
    exit 1
else  
    logSuccess "CallLimiterWeb installation was successful. Version: $call_limiterweb_version_number"
fi

# -------------------------- Change Logging settings ------------------------- #
logInfo "Change Logging settings."

# Enable mysql error log
sudo sed -i 's/#log_error = \/var\/log\/mysql\/error.log/log_error = \/var\/log\/mysql\/error.log/' /etc/mysql/mariadb.conf.d/50-server.cnf

# Create kamailio log rotation settings
cat << EOF | sudo tee /etc/logrotate.d/kamailio > /dev/null
/var/log/kamailio/kamailio.log {
        daily
        missingok
        rotate 7
        compress
        notifempty
}
EOF

# Change existing log rotation settings
apps=("nginx" "redis-server" "mariadb")

for app in "${apps[@]}"; do
  file="/etc/logrotate.d/$app"
  if [ -f "$file" ]; then
    # weekly to daily
    sed -i 's/weekly/daily/g' "$file"
    # monthly to daily
    sed -i 's/monthly/daily/g' "$file"
    # 7 days
    sed -i 's/rotate [0-9]*/rotate 7/g' "$file"
    # enable compress
    if ! grep -q 'compress' "$file"; then
      echo 'compress' >> "$file"
    fi
    # delete maxsize and minsize configuration
    sed -i '/maxsize/d' "$file"
    sed -i '/minsize/d' "$file"
  fi
done

########################################################################################################################
#
# Create self-signed certificate
#
########################################################################################################################
logInfo "Creating self-signed certificate...This may take a while..."
/usr/bin/expect <<EOF
set timeout 3
log_user 0
spawn env LANG=C /usr/bin/openssl req -x509 -nodes -days 365 -newkey rsa:2048 -keyout /etc/ssl/private/server.key -out /etc/ssl/certs/server.crt
expect "Country Name (2 letter code) \[AU\]:"
send "US\r"
expect "State or Province Name (full name) \[Some-State\]:"
send "Some-State\r"
expect "Locality Name (eg, city) \[\]:"
send ".\r"
expect "Organization Name (eg, company) \[Internet Widgits Pty Ltd\]:"
send "$orgname\r"
expect "Organizational Unit Name (eg, section) \[\]:"
send ".\r"
expect "Common Name (e.g. server FQDN or YOUR name) \[\]:"
send "$DOMAIN_NAME\r"
expect "Email Address \[\]:"
send "CallLimiter@example.com\r"
expect eof
EOF

# ------------------------------- Create group ------------------------------- #
sudo groupadd clm-ssl-cert
sudo usermod -a -G clm-ssl-cert kamailio
sudo usermod -a -G clm-ssl-cert nginx
sudo usermod -a -G clm-ssl-cert $username
sudo usermod -a -G clm-ssl-cert www-data


# ---------------------------- Create ca.crt file ---------------------------- #
sudo touch ./ca.crt
sudo chown root:clm-ssl-cert ./ca.crt
sudo chmod 640 ./ca.crt

# ------------------------ Fetch Genesys Cloud CA file ----------------------- #
echo ""
fetchGCCA=true
logInfo "Fetch Genesys Cloud CA file"
if ! curl -sSL https://cacerts.digicert.com/DigiCertHighAssuranceEVRootCA.crt.pem -o GC_DigiCertHighAssuranceEVRootCA.crt.pem; then
    logError "Failed to fetch Genesys Cloud CA file. URL has been changed?"
    fetchGCCA=false
    else
    sudo chown root:clm-ssl-cert ./GC_DigiCertHighAssuranceEVRootCA.crt.pem
    sudo chmod 640 ./GC_DigiCertHighAssuranceEVRootCA.crt.pem
fi

# --------------------------- Fetch twilio CA file --------------------------- #
fetchTwilioCA=true
logInfo "Fetch twilio CA file"
if ! curl -sSL https://www.twilio.com/docs/documents/586/ca-bundle-sip.crt -o twilio_ca-bundle-sip.crt; then
    logError "Failed to fetch twillio CA file. URL has been changed?"
    fetchTwilioCA=false
    else
    sudo chown root:clm-ssl-cert ./twilio_ca-bundle-sip.crt
    sudo chmod 640 ./twilio_ca-bundle-sip.crt
fi

# --------------------------- Fetch vonage CA file --------------------------- #
fetchVonageCA=true
logInfo "Fetch vonage CA file"
if ! curl -sSL https://cacerts.digicert.com/DigiCertTLSRSASHA2562020CA1-1.crt.pem -o vonage_DigiCertTLSRSASHA2562020CA1-1.crt.pem; then
    logError "Failed to fetch vonage CA file. URL has been changed?"
    fetchVonageCA=false
    else
    sudo chown root:clm-ssl-cert ./vonage_DigiCertTLSRSASHA2562020CA1-1.crt.pem
    sudo chmod 640 ./vonage_DigiCertTLSRSASHA2562020CA1-1.crt.pem
fi




# -------------------------- Copy certificate files -------------------------- #
if [ -f /etc/ssl/private/server.key ] && [ -f /etc/ssl/certs/server.crt ]; then
    logSuccess "Self-signed certificate succesfully created."

    cat ./GC_DigiCertHighAssuranceEVRootCA.crt.pem | sudo tee -a ./ca.crt >/dev/null
    cat ./twilio_ca-bundle-sip.crt | sudo tee -a ./ca.crt >/dev/null
    cat ./vonage_DigiCertTLSRSASHA2562020CA1-1.crt.pem | sudo tee -a ./ca.crt >/dev/null

    sudo cp ./ca.crt /etc/ssl/certs/ca.crt
    sudo chown root:clm-ssl-cert /etc/ssl/private/server.key
    sudo chown root:clm-ssl-cert /etc/ssl/certs/server.crt
    sudo chown root:clm-ssl-cert /etc/ssl/certs/ca.crt
    sudo chown root:clm-ssl-cert /etc/ssl/private  
    sudo chown root:clm-ssl-cert /etc/ssl/certs  
    sudo chmod 750 /etc/ssl/private  
    sudo chmod 750 /etc/ssl/certs  
    sudo chmod 640 /etc/ssl/private/server.key
    sudo chmod 640 /etc/ssl/certs/server.crt
    #sudo chmod 640 /etc/ssl/certs/ca.crt

    else
    logError "Failed to create Self-signed certificate."
fi 

########################################################################################################################
#
# Setup kamailio
#
########################################################################################################################
logInfo "Configuring kamailio..."

# ------------------------- Backup kamailio.cfg file ------------------------- #
if [ ! -f /etc/kamailio/backupCLM-kamailio.cfg ]; then  
    cp /etc/kamailio/kamailio.cfg /etc/kamailio/backupCLM-kamailio.cfg
    logSuccess "Backed up original kamailio config file /etc/kamailio/kamailio.cfg"
fi  

# ---------------------------- Backup tls.cfg file --------------------------- #
if [ ! -f /etc/kamailio/backupCLM-tls.cfg ]; then
    cp /etc/kamailio/tls.cfg /etc/kamailio/backupCLM-tls.cfg
    logSuccess "Backed up original kamailio tls config file /etc/kamailio/tls.cfg"
fi

# ------------------------------ Update tls.cfg ------------------------------ #
if [ -f /etc/kamailio/tls.cfg ]; then
    #sed -i 's|verify_certificate = no|verify_certificate = yes|g' /etc/kamailio/tls.cfg
    sed -i 's|private_key = /etc/kamailio/kamailio-selfsigned.key|private_key = /etc/ssl/private/server.key|g' /etc/kamailio/tls.cfg
    sed -i 's|certificate = /etc/kamailio/kamailio-selfsigned.pem|certificate = /etc/ssl/certs/server.crt|g' /etc/kamailio/tls.cfg
    sed -i 's|#ca_list = /etc/kamailio/tls/cacert.pem|ca_list = /etc/ssl/certs/ca.crt|g' /etc/kamailio/tls.cfg
    awk -v n=35 -v s="ca_list = /etc/ssl/certs/ca.crt" 'NR == n {print s} {print}' /etc/kamailio/tls.cfg > temp && mv temp /etc/kamailio/tls.cfg
    logSuccess "Updated /etc/kamailio/tls.cfg"
else
    logWarn "/etc/kamailio/tls.cfg does not exist."
fi

# --------------------------- Backup kamctlrc file --------------------------- #
cp /etc/kamailio/kamctlrc /etc/kamailio/backup-kamctlrc

# ---------------------- Replace SIP_DOMAIN in kamctlrc ---------------------- #
sed -i "s/# SIP_DOMAIN=kamailio.org/SIP_DOMAIN=$localIP/" /etc/kamailio/kamctlrc

# ---------------------- Uncomment DBENGINE in kamctlrc ---------------------- #
sed -i "s/# DBENGINE=MYSQL/DBENGINE=MYSQL/" /etc/kamailio/kamctlrc  

# ---------------------- Auto answer to kamdbctl create ---------------------- #
expect -c "
set timeout 3
log_user 0
spawn env LANG=C /usr/sbin/kamdbctl create
expect \"MySQL password for root:\"
send \"kamailiorw\n\"
expect {
    \"Enter character set name:\" {
        send \"utf32\n\"
        exp_continue
    }
    \"Can't create database 'kamailio'; database exists\" {
        puts \"Error: Database already exists.\"
        exit 1
    }
    \"Create the presence related tables? (y/n):\" {
        send \"y\n\"
        exp_continue
    }
}
expect \"rtpproxy rtpengine secfilter? (y/n):\"
send \"y\n\"
expect \"uid_uri_db? (y/n):\"
send \"y\n\"

expect \"INFO: UID tables successfully created.\"
exit 0
"
# --------------- Create tables for CallLimiter in kamailio DB --------------- #
logInfo "Creating tables for CallLimiter in kamailio database..."

USER='kamailio'
PASSWORD='kamailiorw'
DATABASE='kamailio'
HOST='localhost'

mysql -h $HOST -u $USER -p$PASSWORD $DATABASE <<"EOF"
CREATE TABLE IF NOT EXISTS `CLM_LimitSettings` (
  `LimitSettingsId` varchar(36) NOT NULL,
  `Description` varchar(50) NOT NULL,
  `DNIS` varchar(20) DEFAULT NULL,
  `IsAllBusy` tinyint(1) NOT NULL,
  `Type` varchar(30) DEFAULT NULL,
  `MaxLimitValue` int(11) NOT NULL,
  `AvailableAgentsLimitValue` int(11) NOT NULL,
  `Conditions` varchar(3) DEFAULT NULL,
  `DivisionId` varchar(36) NOT NULL,
  `DivisionName` varchar(50) NOT NULL,
  `IsScheduled` tinyint(1) NOT NULL,
  `ScheduleStart` datetime(6) NOT NULL,
  `ScheduleEnd` datetime(6) NOT NULL,
  `ScheduleStartString` longtext NOT NULL,
  `ScheduleENDPT` longtext NOT NULL,
  `IsEnabled` tinyint(1) NOT NULL,
  `IsSettingsError` tinyint(1) NOT NULL,
  `DateCreated` datetime(6) NOT NULL,
  `DateModified` datetime(6) NOT NULL,
  `CreatedBy` varchar(50) NOT NULL,
  `LastModifiedBy` varchar(50) NOT NULL,
  PRIMARY KEY (`LimitSettingsId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `CLM_QueueListLMS` (
  `QueueListId` int(11) NOT NULL AUTO_INCREMENT,
  `LimitSettingsId` varchar(36) NOT NULL,
  `QueueId` varchar(36) NOT NULL,
  `QueueName` varchar(255) NOT NULL,
  PRIMARY KEY (`QueueListId`),
  KEY `IX_CLM_QueueListLMS_LimitSettingsId` (`LimitSettingsId`),
  CONSTRAINT `FK_CLM_QueueListLMS_CLM_LimitSettings_LimitSettingsId` FOREIGN KEY (`LimitSettingsId`) REFERENCES `CLM_LimitSettings` (`LimitSettingsId`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=105 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `CLM_DNISListLMS` (
  `DNISListId` int(11) NOT NULL AUTO_INCREMENT,
  `DNIS` varchar(20) NOT NULL,
  `LimitSettingsId` varchar(36) NOT NULL,
  PRIMARY KEY (`DNISListId`),
  KEY `IX_CLM_DNISListLMS_LimitSettingsId` (`LimitSettingsId`),
  CONSTRAINT `FK_CLM_DNISListLMS_CLM_LimitSettings_LimitSettingsId` FOREIGN KEY (`LimitSettingsId`) REFERENCES `CLM_LimitSettings` (`LimitSettingsId`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=36 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `CLM_ANIList` (
  `ANI` varchar(20) DEFAULT NULL,
  `DateCreated` datetime(6) NOT NULL,
  `CreatedBy` varchar(50) NOT NULL,
  PRIMARY KEY (`ANI`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE EVENT IF NOT EXISTS CLM_clean_old_acc_records  
ON SCHEDULE EVERY 1 DAY STARTS CURDATE() + INTERVAL 1 DAY  
DO  
  DELETE FROM acc WHERE time < DATE_SUB(NOW(), INTERVAL 2 MONTH); 
EOF
logSuccess "Tables CLM_LimitSettings,CLM_QueueListLMS and CLM_DNISListLMS were successfully created."

COLUMN_EXISTS=$(mysql -h $HOST -u $USER -p$PASSWORD $DATABASE -Bse "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '$DATABASE' AND TABLE_NAME = 'acc' AND COLUMN_NAME = 'src_user'")
if [ "$COLUMN_EXISTS" -eq "0" ]; then
  mysql -h $HOST -u $USER -p$PASSWORD $DATABASE <<"EOF"
  ALTER TABLE acc ADD COLUMN src_user VARCHAR(64) NOT NULL DEFAULT '';
  ALTER TABLE acc ADD COLUMN src_domain VARCHAR(128) NOT NULL DEFAULT '';
  ALTER TABLE acc ADD COLUMN src_ip varchar(64) NOT NULL default '';
  ALTER TABLE acc ADD COLUMN dst_ouser VARCHAR(64) NOT NULL DEFAULT '';
  ALTER TABLE acc ADD COLUMN dst_user VARCHAR(64) NOT NULL DEFAULT '';
  ALTER TABLE acc ADD COLUMN dst_domain VARCHAR(128) NOT NULL DEFAULT '';
  ALTER TABLE acc ADD COLUMN limitSettingsId varchar(36) DEFAULT NULL;
  ALTER TABLE acc ADD COLUMN divisionId varchar(36) DEFAULT NULL;
EOF
logSuccess "Added columns (src_user,src_domain,src_ip,dst_ouser,dst_user,dst_domain,limitSettingsId,divisionId) to acc table."
fi

echo "[mysqld]" >> /etc/mysql/my.cnf
echo "event_scheduler=ON" >> /etc/mysql/my.cnf
logInfo "The 'acc' table for logging blocked call details is configured to retain data for a maximum of 60 days."

# -------- Restart mariadb to apply changes and check if it's running -------- #
if sudo systemctl restart mariadb; then
    logSuccess "mariadb successfully restarted"
else
    logError "Failed to restart mariadb"
    exit 1
fi

# ---------------------------- Set Unlimited mode ---------------------------- #
logInfo "Set Unlimited mode in Redis."
redis-cli SET systemSettings "Unlimited"

# -------------------- Fetch kamailio.cfg for CallLimiter -------------------- #
if ! curl -sSL https://raw.githubusercontent.com/tishige/CLM/main/kamailio.cfg -o kamailio.cfg; then
    logError "Failed to download kamailio.cfg"
    exit 1
fi

# -------------------------- Enable kamailio logging ------------------------- #
if ! grep -q "local0.* -/var/log/kamailio/kamailio.log" /etc/rsyslog.d/50-default.conf; then
    echo "local0.* -/var/log/kamailio/kamailio.log" >> /etc/rsyslog.d/50-default.conf
fi
sudo mkdir /var/log/kamailio
touch /var/log/kamailio/kamailio.log
sudo chown syslog:adm /var/log/kamailio/kamailio.log
chmod 644 /var/log/kamailio/kamailio.log
systemctl restart rsyslog

# ------------------------ Update kamailio config file ----------------------- #
if [ $protocol == 1 ]; then
    if [ "$usepublicIP" = true ]; then
        sed -i "/# listen=udp:10.0.0.10:5060/i listen=udp:$localIP:$port advertise $publicIP:$port" kamailio.cfg
        echo ""
        echo ""
        logInfo "Added $localIP and advertise address $publicIP in kamailio.cfg file"
    else
        sed -i "/# listen=udp:10.0.0.10:5060/i listen=udp:$localIP:$port" kamailio.cfg
        echo ""
        echo ""
        logInfo "Added $localIP in kamailio.cfg file"
    fi
elif [ $protocol == 2 ]; then
    if [ "$usepublicIP" = true ]; then
        sed -i "/# listen=udp:10.0.0.10:5060/i listen=tcp:$localIP:$port advertise $publicIP:$port" kamailio.cfg
        echo ""
        echo ""
        logInfo "Added $localIP and advertise address $publicIP in kamailio.cfg file"
    else
        sed -i "/# listen=udp:10.0.0.10:5060/i listen=tcp:$localIP:$port" kamailio.cfg
        echo ""
        echo ""
        logInfo "Added $localIP in kamailio.cfg file"
    fi
else  
    if [ "$usepublicIP" = true ]; then
        sed -i "/# listen=udp:10.0.0.10:5060/i listen=tls:$localIP:$port advertise $publicIP:$port" kamailio.cfg
        sed -i "/# listen=udp:10.0.0.10:5060/a #listen=udp:$localIP:5060 advertise $publicIP:5060" kamailio.cfg
        echo ""
        echo ""
        logInfo "TLS enabled.Added $localIP and advertise address $publicIP in kamailio.cfg file"
    else
        sed -i "/# listen=udp:10.0.0.10:5060/i listen=tls:$localIP:$port" kamailio.cfg
        sed -i "/# listen=udp:10.0.0.10:5060/a #listen=udp:$localIP:5060" kamailio.cfg
        echo ""
        echo ""
        logInfo "TLS enabled.Added $localIP in kamailio.cfg file"
    fi

fi

# ----------------- Uncomment WITH_TLS to use TLS connections ---------------- #
if [ "$protocol" -eq 3 ]; then
    sed -i "s/##!define WITH_TLS/#!define WITH_TLS/" kamailio.cfg
    logInfo "Enabled TLS in kamailio.cfg file"
fi

cp kamailio.cfg /etc/kamailio/kamailio.cfg
chown root:root /etc/kamailio/kamailio.cfg
chmod 644 /etc/kamailio/kamailio.cfg

logSuccess "kamailio.cfg file updated"

# ----------------------------- Restart kamailio ----------------------------- #
if systemctl restart kamailio; then
    logSuccess "kamailio successfully restarted"
else
    logError "Failed to restart kamailio"
    exit 1
fi

# ------------------ Configure kamailio dispatcher settings ------------------ #
for i in "${!lines[@]}"
do
    line=${lines[i]}
    comment=${comments[i]}
    # Check if line contains a port number
    if [[ $line =~ :[0-9]+$ ]]; then
        if [ $protocol -eq 2 ]; then
            line="${line};transport=tcp"
        elif [ $protocol -eq 3 ]; then
            line="${line};transport=tls"
        fi  
    else  
        if [ $protocol -eq 2 ]; then
            line="${line}:5060;transport=tcp"
        elif [ $protocol -eq 3 ]; then
            line="${line}:5061;transport=tls"
        fi
    fi

    expect -c "
    set timeout 5
    log_user 0
    # Enclose sip:$line with quotes
    spawn env LANG=C /usr/sbin/kamctl dispatcher add 1 \"sip:$line\" 0 0 \"\" \"$comment\"
    expect \"*password for user 'kamailio@localhost':\"
    send \"kamailiorw\r\"
    expect eof
    "
    if [ $? -eq 0 ]
    then
        logSuccess "kamctl dispatcher add command was successful"
    else
        logError "kamctl dispatcher add command failed"
        exit 1
    fi
done

logInfo "kamctl dispatcher show' results:"
sudo kamctl dispatcher show
sudo kamctl dispatcher reload

# ------------------------- Check Edge Servers status ------------------------ #
logInfo "Checking EDGE Server connection status..."
sleep 10

output=$(sudo kamcmd dispatcher.list)

echo "$output" | while read -r line ; do
    if [[ $line == *"URI:"* ]]; then
        uri=$(echo $line | cut -d ' ' -f 2)
    elif [[ $line == *"FLAGS:"* ]]; then
        flag=$(echo $line | cut -d ' ' -f 2)
        if [[ $flag == "AP" ]]; then
            status="${GREEN}In Service${DEFAULT}"
        elif [[ $flag == "AX" ]]; then
            status="${YELLOW}Out of Service${DEFAULT}"
        else
            status="${RED}INACTIVE${DEFAULT}"
        fi
        echo -e "URI: $uri Status: $status"
    fi
done

########################################################################################################################
#
# Modify CallLimittr appsettings and start services
#
########################################################################################################################
sed -i "s|YOURCLIENTID|$clientId|g" /usr/local/CallLimiter/appsettings.json
sed -i "s|YOURCLIENTSECRET|$clientSecret|g" /usr/local/CallLimiter/appsettings.json
sed -i "s|PURECLOUDENVIROMENT|$environment|g" /usr/local/CallLimiter/appsettings.json

sed -i "s|YOURCLIENTID|$clientIdCA|g" /usr/local/CallLimiterWeb/appsettings.json
sed -i "s|YOURCLIENTSECRET|$clientSecretCA|g" /usr/local/CallLimiterWeb/appsettings.json
sed -i "s|PURECLOUDENVIROMENT|$environment|g" /usr/local/CallLimiterWeb/appsettings.json
sed -i "s|YOURORGSHORTNAME|$orgname|g" /usr/local/CallLimiterWeb/appsettings.json
sed -i "s|YOURCallLimiterWEBSITEADDRESS|$redirectURL|g" /usr/local/CallLimiterWeb/appsettings.json

# --------------------- Start CallLimiter Server and web --------------------- #
systemctl daemon-reload
systemctl enable CallLimiter.service
systemctl start CallLimiter.service
systemctl enable CallLimiterWeb.service
systemctl start CallLimiterWeb.service

# ------------------ Check if CallLimiter.service is active ------------------ #
if systemctl --quiet is-active CallLimiter.service
then
  logSuccess "CallLimiter service is active. Installation was successful."
else
  logError "CallLimiter service is not active. Installation failed."
  exit 1
fi

########################################################################################################################
#
# Create nginx config file
#
########################################################################################################################
logInfo "Creating nginx configration file"

cat << EOF | sudo tee /etc/nginx/conf.d/CallLimiter.conf > /dev/null
server {
    listen 80;
    listen [::]:80;
    server_name $DOMAIN_NAME;
    return 301 https://$server_name\$request_uri;
}

server {
    listen 443 ssl;
    server_name $DOMAIN_NAME;

    ssl_certificate /etc/ssl/certs/server.crt;
    ssl_certificate_key /etc/ssl/private/server.key;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection upgrade;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_cache_bypass \$http_upgrade;
    }
}
EOF

# --------- Restart Nginx to apply changes and check if it's running --------- #
if sudo systemctl restart nginx; then
    logSuccess "nginx successfully restarted"
else
    logError "Failed to restart nginx"
    exit 1
fi
logInfo "Checking CallLimiter Web page..."
sleep 10

# ------------------ Check if CallLimiter web site is active ----------------- #
if curl -k -s -m 10 https://$DOMAIN_NAME | grep -q '<title>GC_CallLimiter</title>'
then
  logSuccess "CallLimiter WebServer is running."
else
  if curl -k -s -m 5 https://localhost:5001 | grep -q '<title>GC_CallLimiter</title>'
  then
    logWarn "CallLimiter Web server is running, but it can't be accessed from external networks. Please check your firewall configuration."
  else
    logError "Unable to open CallLimiter Web page."
  fi
fi

########################################################################################################################
#
# Show installation results
#
########################################################################################################################
echo ""
logWarn "============================ Installation result ==========================="
echo ""
logInfo "--------------------------- CallLimiter web site --------------------------"
logDebug "https://$DOMAIN_NAME/GCLogin/Index"
echo ""
logInfo "------------------------------- SIP settings -------------------------------"
logDebug "Local IP address : $localIP"
if $usepublicIP; then
  logDebug "IP address for SIP Communication : $publicIP"
else
  logDebug "IP address for SIP Communication : $localIP"
fi
logDebug "Domain name : $DOMAIN_NAME"
if [ $protocol == 1 ]; then  
    logDebug "Use UDP port : $port"  
elif [ $protocol == 2 ]; then  
    logDebug "Use TCP port : $port"  
else  
    logDebug "Use TLS port : $port"  
fi  

output=$(sudo kamcmd dispatcher.list)  
outOfService=false
byoc=false
while read -r line ; do  
    if [[ $line == *"URI:"* ]]; then  
        uri=$(echo $line | cut -d ' ' -f 2)
        if [[ $uri == *"mypurecloud"* ]]; then
            byoc=true
        fi
    elif [[ $line == *"FLAGS:"* ]]; then
        flag=$(echo $line | cut -d ' ' -f 2)
        if [[ $flag == "AP" ]]; then
            status="${GREEN}In Service${DEFAULT}"
        elif [[ $flag == "AX" ]]; then
            status="${YELLOW}Out of Service${DEFAULT}"
            outOfService=true  
        else
            status="${RED}INACTIVE${DEFAULT}"
        fi  
        echo -e "URI: $uri Status: $status"
    fi
done < <(echo "$output")

if $outOfService; then
    logErrorWhite "Please check your Edge status or add this server address to the [SIP Servers or Proxies] settings in the External Trunk."
fi

echo ""
logInfo "---------------------------- Application Version ---------------------------"
logDebug "redis          : $redis_version_number"
logDebug "nginx          : $nginx_version_number"
logDebug "mariadb        : $mariadb_version_number"
logDebug "kamailio       : $kamailio_version_number"
logDebug "CallLimiter    : $call_limiter_version_number"
logDebug "CallLimiterWeb : $call_limiterweb_version_number"
echo ""
logInfo "-------------------------- Genesys Cloud Settings --------------------------"
logDebug "Genesys Cloud Enviroment : $environment"
logDebug "Genesys Cloud Orgization name : $orgname"
logDebug "Client Credentials ClientId : $clientId"
logDebug "Client Credentials ClientSecret : $clientSecret"
logDebug "Saved in appSettings.json : /usr/local/CallLimiter/appSettings.json"
echo ""
logDebug "Code Authorization ClientId : $clientIdCA"
logDebug "Code Authorization ClientSecret : $clientSecretCA"
logDebug "Redirect URL : $redirectURL"
logDebug "Saved in appSettings.json : /usr/local/CallLimiterWeb/appSettings.json"
echo ""
logInfo "----------------------------- maria DB Settings ----------------------------"
logDebug "Database : kamailio"
logDebug "root password : kamailiorw"
logDebug "user : kamailio password : kamailiorw"
logDebug "user : kamailioro password : kamailioro"
logDebug "Tables for CallLimiter in kamailio database"
logDebug "CLM_ANIList"
logDebug "CLM_DNISListLMS"
logDebug "CLM_LimitSettings"
logDebug "CLM_QueueListLMS"
echo ""
logInfo "---------------------------------- Logging ---------------------------------"
logDebug "The 'acc' table for logging blocked call details is configured to retain data for a maximum of 60 days."
logDebug "The log files for the external applications (MariaDB, Nginx, Redis-server, Kamailio) used by CallLimiter are retained in the /var/log directory for a duration of 7 days."
echo ""
logInfo "-------------------------- Self-signed certificate -------------------------"
if [ $protocol == 3 ]; then 
logError "Please copy your trusted CA-signed certificate files to the following directory and then restart both kamairio and nginx."
fi
logDebug "Self-signed server key saved as /etc/ssl/private/server.key"
logDebug "Self-signed server certificate saved as /etc/ssl/certs/server.crt"
logDebug "Appended the following CA files to /etc/ssl/certs/ca.crt"
if [ $fetchGCCA = true ]; then
    logDebug "Genesys Cloud CA Success"
else
    logError "Genesys Cloud CA Failed"
fi
if [ $fetchTwilioCA = true ]; then
    logDebug "Twilio CA Success"
else
    logError "Twilio CA Failed"
fi
if [ $fetchVonageCA = true ]; then
    logDebug "Vonage CA Success"
else
    logError "Vonage CA Failed"
fi
logWarn "Please reconnect your terminal session once to apply the changes."
logWarn "You can review the above installation results in CLM_Install_Result.txt."
echo ""
logSuccess "CallLimiter installation successfully completed!"

# --------------------------- Write results to file -------------------------- #
result_write ""
result_write "============================ Installation result ==========================="
result_write ""
result_write "--------------------------- CallLimiter web site --------------------------"
result_write "https://$DOMAIN_NAME/GCLogin/Index"
result_write ""
result_write "------------------------------- SIP settings -------------------------------"
result_write "Local IP address : $localIP"
if $usepublicIP; then
  result_write "IP address for SIP Communication : $publicIP"
else
  result_write "IP address for SIP Communication : $localIP"
fi
result_write "Domain name : $DOMAIN_NAME"
if [ $protocol == 1 ]; then  
    result_write "Use UDP port : $port"  
elif [ $protocol == 2 ]; then  
    result_write "Use TCP port : $port"  
else  
    result_write "Use TLS port : $port"  
fi  

output=$(sudo kamcmd dispatcher.list)  
outOfService=false
byoc=false
while read -r line ; do  
    if [[ $line == *"URI:"* ]]; then  
        uri=$(echo $line | cut -d ' ' -f 2)
        if [[ $uri == *"mypurecloud"* ]]; then
            byoc=true
        fi
    elif [[ $line == *"FLAGS:"* ]]; then
        flag=$(echo $line | cut -d ' ' -f 2)
        if [[ $flag == "AP" ]]; then
            status="In Service"
        elif [[ $flag == "AX" ]]; then
            status="Out of Service"
            outOfService=true  
        else
            status="INACTIVE"
        fi  
        result_write -e "URI: $uri Status: $status"
    fi
done < <(echo "$output")

if $outOfService; then
    result_write "Please check your Edge status or add this server address to the [SIP Servers or Proxies] settings in the External Trunk."
fi
result_write ""
result_write "---------------------------- Application Version ---------------------------"
result_write "redis          : $redis_version_number"
result_write "nginx          : $nginx_version_number"
result_write "mariadb        : $mariadb_version_number"
result_write "kamailio       : $kamailio_version_number"
result_write "CallLimiter    : $call_limiter_version_number"
result_write "CallLimiterWeb : $call_limiterweb_version_number"
result_write ""
result_write "-------------------------- Genesys Cloud Settings --------------------------"
result_write "Genesys Cloud Enviroment : $environment"
result_write "Genesys Cloud Orgization name : $orgname"
result_write "Client Credentials ClientId : $clientId"
result_write "Client Credentials ClientSecret : $clientSecret"
result_write "Saved in appSettings.json : /usr/local/CallLimiter/appSettings.json"
result_write ""
result_write "Code Authorization ClientId : $clientIdCA"
result_write "Code Authorization ClientSecret : $clientSecretCA"
result_write "Redirect URL : $redirectURL"
result_write "Saved in appSettings.json : /usr/local/CallLimiterWeb/appSettings.json"
result_write ""
result_write "----------------------------- maria DB Settings ----------------------------"
result_write "Database : kamailio"
result_write "root password : kamailiorw"
result_write "user : kamailio password : kamailiorw"
result_write "user : kamailioro password : kamailioro"
result_write "Tables for CallLimiter in kamailio database"
result_write "CLM_ANIList"
result_write "CLM_DNISListLMS"
result_write "CLM_LimitSettings"
result_write "CLM_QueueListLMS"
result_write ""
result_write "---------------------------------- Logging ---------------------------------"
result_write "The 'acc' table for logging blocked call details is configured to retain data for a maximum of 60 days."
result_write "The log files for the external applications (MariaDB, Nginx, Redis-server, Kamailio) used by CallLimiter are retained in the /var/log directory for a duration of 7 days."
result_write ""
result_write "-------------------------- Self-signed certificate -------------------------"
if [ $protocol == 3 ]; then 
result_write "Please copy your trusted CA-signed certificate files to the following directory and then restart both kamairio and nginx."
fi
result_write "Self-signed server key saved as /etc/ssl/private/server.key"
result_write "Self-signed server certificate saved as /etc/ssl/certs/server.crt"
result_write "Appended the following CA files to /etc/ssl/certs/ca.crt"
if [ $fetchGCCA = true ]; then
    result_write "Genesys Cloud CA Success"
else
    result_write "Genesys Cloud CA Failed"
fi
if [ $fetchTwilioCA = true ]; then
    result_write "Twilio CA Success"
else
    result_write "Twilio CA Failed"
fi
if [ $fetchVonageCA = true ]; then
    result_write "Vonage CA Success"
else
    result_write "Vonage CA Failed"
fi
result_write ""
result_write "CallLimiter installation successfully completed!"
