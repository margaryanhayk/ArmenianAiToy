import { useEffect, useState } from 'react';
import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { StatusBar } from 'expo-status-bar';
import { clearToken, getToken, saveToken } from './src/auth';
import { LinkedDevice } from './src/api';
import LoginScreen from './src/screens/LoginScreen';
import DevicesScreen from './src/screens/DevicesScreen';
import ConversationsScreen from './src/screens/ConversationsScreen';
import ConversationDetailScreen from './src/screens/ConversationDetailScreen';

type Screen =
  | { name: 'devices' }
  | { name: 'conversations'; deviceId: string; deviceName: string }
  | { name: 'conversationDetail'; conversationId: string; deviceId: string; deviceName: string };

function AuthedNavigator({ onLogout }: { onLogout: () => void }) {
  const [screen, setScreen] = useState<Screen>({ name: 'devices' });

  if (screen.name === 'devices') {
    return (
      <DevicesScreen
        onLogout={onLogout}
        onOpenDevice={(d: LinkedDevice) =>
          setScreen({ name: 'conversations', deviceId: d.deviceId, deviceName: d.deviceName })
        }
      />
    );
  }

  if (screen.name === 'conversations') {
    return (
      <ConversationsScreen
        deviceId={screen.deviceId}
        deviceName={screen.deviceName}
        onBack={() => setScreen({ name: 'devices' })}
        onOpenConversation={(conversationId: string) =>
          setScreen({
            name: 'conversationDetail',
            conversationId,
            deviceId: screen.deviceId,
            deviceName: screen.deviceName,
          })
        }
        onLogout={onLogout}
      />
    );
  }

  return (
    <ConversationDetailScreen
      conversationId={screen.conversationId}
      onBack={() =>
        setScreen({ name: 'conversations', deviceId: screen.deviceId, deviceName: screen.deviceName })
      }
      onLogout={onLogout}
    />
  );
}

export default function App() {
  const [booting, setBooting] = useState(true);
  const [signedIn, setSignedIn] = useState(false);

  // Restore an existing session on launch.
  useEffect(() => {
    (async () => {
      const token = await getToken();
      setSignedIn(!!token);
      setBooting(false);
    })();
  }, []);

  async function handleLoggedIn(token: string) {
    await saveToken(token);
    setSignedIn(true);
  }

  async function handleLogout() {
    await clearToken();
    setSignedIn(false);
  }

  if (booting) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color="#2c4a7a" />
        <StatusBar style="auto" />
      </View>
    );
  }

  return (
    <>
      {signedIn ? (
        <AuthedNavigator onLogout={handleLogout} />
      ) : (
        <LoginScreen onLoggedIn={handleLoggedIn} />
      )}
      <StatusBar style="auto" />
    </>
  );
}

const styles = StyleSheet.create({
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: '#fff' },
});
