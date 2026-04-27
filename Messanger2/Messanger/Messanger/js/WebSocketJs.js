const wsProtocol = window.location.protocol === "https:" ? "wss" : "ws";
const socket = new WebSocket(`${wsProtocol}://${window.location.host}/ws`);
const write_message = document.getElementById("write_message");
const send_message = document.getElementById("send_message");
const all_message = document.getElementById("all_message");

const url = new URL(req.url, 'https://localhost').toString();
const UserName2 = url?.split('/')[2]?.slice(0, -1);

send_message.addEventListener("click", function() {
    const message = write_message.value;
    if (message.trim !== "" && socket.get(UserName2).readyState === socket.OPEN) {
        socket.get(UserName2).send(message);
        write_message.value = "";
    } 
    if (message.trim !== "" && socket.readyState === socket.OPEN) {
        socket.send(message);
        write_message.value = "";
    }
});

socket.onmessage = function(event) {
   all_message.value += event.data + "\n";
   all_message.scrollTop = all_message.scrollHeight;
};

socket.onclose = function () {
    alert("Соединение закрыто!");
};

close.addEventListener("click", function () {
    socket.close(1000, "Заершение работы");
});